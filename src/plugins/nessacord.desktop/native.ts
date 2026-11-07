/*
 * Vencord, a Discord client mod
 * Copyright (c) 2026 deargod and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

// Nessacord, main-process side: window/taskbar icon, window title, tray icon and tooltip, splash
// logo, shortcuts and (opt-in) the Task Manager name. Based on deargod's GodCord.
//
// Nothing is exported on purpose: every export of a native.ts is registered as an IPC handler.
// Discord's userData path is never touched (no app.setName), so logins and settings stay put.

import { RendererSettings } from "@main/settings";
import { DATA_DIR, IS_VANILLA } from "@main/utils/constants";
import { spawn } from "child_process";
import { app, BrowserWindow, NativeImage, nativeImage, shell, ShortcutDetails, Tray } from "electron";
import fs, { existsSync, mkdirSync, readdirSync, readFileSync, renameSync, rmSync, watch, writeFileSync } from "fs";
import { basename, dirname, join, sep } from "path";

import { ICON_ICO_BASE64, MARK_256_DATA_URI, TRAY_ICONS } from "./iconData";

const APP_NAME = "Nessacord";
const ICO_PATH = join(DATA_DIR, "nessacord.ico");

function pluginSettings() {
    return RendererSettings.store.plugins?.Nessacord;
}

function isEnabled() {
    // enabledByDefault: on the very first launch the plugin has no settings entry yet.
    return pluginSettings()?.enabled !== false;
}

// ── Task Manager (opt-in) ─────────────────────────────────────────────────────────────────────
// Task Manager names an app after its exe's description, so showing Nessacord there means running
// Discord from Nessacord.exe: a copy of Discord's exe with Nessacord's name and icon, made by the
// installer for each Discord version. Discord's launchers always start Discord's own exe, so
// Nessacord hands over to Nessacord.exe here, before any of Discord's code runs. Off by default:
// Krisp noise suppression only runs from Discord's own signed exe.
const BRANDED_EXE = "Nessacord.exe";

function useBrandedExe() {
    const appFolder = dirname(process.execPath);
    const branded = join(appFolder, BRANDED_EXE);

    if (basename(process.execPath).toLowerCase() === BRANDED_EXE.toLowerCase()) {
        // Discord's own code keeps seeing its own exe, which it uses for shortcuts, autostart and
        // restarts after updates.
        const discordExe = readdirSync(appFolder).find(f => /^Discord\w*\.exe$/i.test(f));
        if (discordExe) process.execPath = join(appFolder, discordExe);
        return;
    }

    if (!isEnabled() || pluginSettings()?.taskManagerName !== true) {
        // Turned off: the copy is ~230 MB.
        if (existsSync(branded)) rmSync(branded);
        return;
    }

    // Squirrel's install and update steps run Discord's exe and expect it to finish them.
    if (process.argv.some(arg => arg.startsWith("--squirrel"))) return;

    if (existsSync(branded)) {
        spawn(branded, process.argv.slice(1), { detached: true, stdio: "ignore" }).unref();
        process.exit(0);
    }

    // A new Discord version: make Nessacord.exe for next time, and carry on from Discord's exe now.
    const installer = join(DATA_DIR, "NessacordInstaller.exe");
    if (existsSync(installer)) spawn(installer, ["--brand-exe", appFolder], { detached: true, stdio: "ignore" }).unref();
}

if (process.platform === "win32" && !IS_VANILLA) {
    try {
        useBrandedExe();
    } catch (err) {
        console.error("[Nessacord] Could not switch to Nessacord.exe", err);
    }
}

// The .ico lives in Nessacord's data folder: Windows needs a file path for shortcut and taskbar icons.
function ensureIconFile() {
    try {
        const ico = Buffer.from(ICON_ICO_BASE64, "base64");
        if (existsSync(ICO_PATH) && readFileSync(ICO_PATH).equals(ico)) return true;
        mkdirSync(DATA_DIR, { recursive: true });
        writeFileSync(ICO_PATH, ico);
        return true;
    } catch (err) {
        console.error("[Nessacord] Could not write the icon file", err);
        return false;
    }
}

const hasIconFile = ensureIconFile();

let icon: NativeImage | undefined;
function getIcon() {
    icon ??= hasIconFile ? nativeImage.createFromPath(ICO_PATH) : nativeImage.createFromDataURL(MARK_256_DATA_URI);
    return icon;
}

// Discord sets com.squirrel.<Name>.<Name> as its AppUserModelID (e.g. com.squirrel.Discord.Discord).
const EXE_NAME = basename(process.execPath, ".exe");
const APP_ID = `com.squirrel.${EXE_NAME}.${EXE_NAME}`;

// Nessacord's windows carry their own AppUserModelID (see Taskbar identity below).
const WINDOW_APP_ID = `${APP_NAME}.${EXE_NAME}`;

// ── Shortcuts ─────────────────────────────────────────────────────────────────────────────────
// Start menu, search and notification headers use the shortcut that carries Discord's
// AppUserModelID, and a taskbar pin draws its own shortcut's icon. Discord writes its stock
// shortcuts back after updates, so they are re-branded at startup and whenever one re-appears:
// - Start menu and desktop: Nessacord icon and name (renamed to Nessacord.lnk; a re-created
//   duplicate is removed). They keep Discord's AppUserModelID, which notifications need.
// - Taskbar pins: Nessacord icon, and Nessacord's window ID so the running window shares the pin's
//   button. Pins are never renamed (Windows tracks them by file).
// Only shortcuts that start this install are touched (another Discord install shares the same
// AppUserModelID). What each one looked like before is kept in shortcuts.json, so turning the
// option off (or uninstalling) puts them back.
const INSTALL_ROOT = dirname(dirname(process.execPath)).toLowerCase() + sep;
const START_MENU_DIR = join(app.getPath("appData"), "Microsoft", "Windows", "Start Menu", "Programs");
const SHORTCUT_DIRS = [
    { dir: START_MENU_DIR, pinned: false },
    { dir: join(START_MENU_DIR, "Discord Inc"), pinned: false },
    { dir: app.getPath("desktop"), pinned: false },
    { dir: join(app.getPath("appData"), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar"), pinned: true }
];
const SHORTCUT_RECORD = join(DATA_DIR, "shortcuts.json");

type OriginalShortcut = Required<Pick<ShortcutDetails, "icon" | "iconIndex" | "description" | "appUserModelId">> & { path: string; };

function readRecord(): Record<string, OriginalShortcut> {
    try {
        return JSON.parse(readFileSync(SHORTCUT_RECORD, "utf8"));
    } catch {
        return {};
    }
}

function readOurShortcut(path: string) {
    try {
        const link = shell.readShortcutLink(path);
        const ours = (link.appUserModelId === APP_ID || link.appUserModelId === WINDOW_APP_ID)
            && link.target.toLowerCase().startsWith(INSTALL_ROOT);
        return ours ? link : undefined;
    } catch {
        return undefined;
    }
}

function brandShortcuts() {
    const record = readRecord();
    const recorded = Object.keys(record).length;

    for (const { dir, pinned } of SHORTCUT_DIRS) {
        let names: string[];
        try {
            names = readdirSync(dir).filter(n => /\.lnk$/i.test(n) && /discord|godcord|nessacord/i.test(n));
        } catch {
            continue;
        }

        for (const name of names) {
            const path = join(dir, name);
            const link = readOurShortcut(path);
            if (!link) continue;

            const brandedPath = pinned ? path : join(dir, `${APP_NAME}.lnk`);
            const isDuplicate = path.toLowerCase() !== brandedPath.toLowerCase() && existsSync(brandedPath);
            if (isDuplicate) {
                if (readOurShortcut(brandedPath)) rmSync(path);
                continue;
            }

            if (link.icon !== ICO_PATH) {
                record[brandedPath.toLowerCase()] ??= {
                    path,
                    icon: link.icon ?? "",
                    iconIndex: link.iconIndex ?? 0,
                    description: link.description ?? "",
                    appUserModelId: link.appUserModelId ?? ""
                };
            }

            const appUserModelId = pinned ? WINDOW_APP_ID : link.appUserModelId;
            if (link.icon !== ICO_PATH || link.description !== APP_NAME || link.appUserModelId !== appUserModelId)
                shell.writeShortcutLink(path, "update", { target: link.target, icon: ICO_PATH, iconIndex: 0, description: APP_NAME, appUserModelId });

            if (path !== brandedPath) renameSync(path, brandedPath);
        }
    }

    if (Object.keys(record).length !== recorded) writeFileSync(SHORTCUT_RECORD, JSON.stringify(record, null, 4));
}

function restoreShortcuts() {
    const record = readRecord();

    for (const [brandedPath, original] of Object.entries(record)) {
        const link = readOurShortcut(brandedPath);
        if (!link) continue;

        const { path, ...details } = original;
        shell.writeShortcutLink(brandedPath, "update", { target: link.target, ...details });

        if (path.toLowerCase() === brandedPath) continue;
        if (existsSync(path)) rmSync(brandedPath);
        else renameSync(brandedPath, path);
    }

    rmSync(SHORTCUT_RECORD, { force: true });
}

function tryBrandShortcuts() {
    try {
        brandShortcuts();
    } catch (err) {
        console.error("[Nessacord] Could not brand the shortcuts", err);
    }
}

if (process.platform === "win32") {
    if (hasIconFile && isEnabled() && pluginSettings()?.brandShortcuts !== false) {
        // At startup, before Discord's first window exists, so its taskbar button resolves to the branded shortcut.
        tryBrandShortcuts();

        // Then whenever Discord writes its own shortcut back. Debounced so the file is complete
        // before it is read; the pass is idempotent.
        let timer: ReturnType<typeof setTimeout> | undefined;
        const onChange = (_event: string, file: string | Buffer | null) => {
            if (file && !/discord.*\.lnk$/i.test(String(file))) return;
            clearTimeout(timer);
            timer = setTimeout(tryBrandShortcuts, 1000);
        };

        for (const { dir } of SHORTCUT_DIRS) {
            try {
                watch(dir, { persistent: false }, onChange).on("error", () => { });
            } catch {
                // Folder missing: nothing to keep branded there.
            }
        }
    } else if (existsSync(SHORTCUT_RECORD)) {
        try {
            restoreShortcuts();
        } catch (err) {
            console.error("[Nessacord] Could not restore the shortcuts", err);
        }
    }
}

// ── Window titles ─────────────────────────────────────────────────────────────────────────────
// Discord's main process titles windows "[(n) |• ]<page> - Discord" (splash: "Discord Updater").
// Only the app-name token is swapped, so server or channel names containing "Discord" are left alone.
const UNREAD_PREFIX = /^(?:\(\d+\) |• )/;
const APP = "Discord(?: (?:Canary|PTB|Development))?";
const WHOLE_APP_NAME = new RegExp(`^${APP}$`);
const TRAILING_APP_NAME = new RegExp(`\\s*-\\s*${APP}$`);
const LEADING_APP_NAME = new RegExp(`^${APP}(?= \\||$| Updater)`);
const PAGE_URL = /^(?:https?:\/\/)?(?:[\w-]+\.)*discord\.com\//;

function brandTitle(raw: string) {
    const prefix = UNREAD_PREFIX.exec(raw)?.[0] ?? "";
    const rest = raw.slice(prefix.length).trim();

    if (!rest || WHOLE_APP_NAME.test(rest)) return prefix + APP_NAME;

    if (TRAILING_APP_NAME.test(rest)) {
        const page = rest.replace(TRAILING_APP_NAME, "").trim();
        // A page without a title (the login screen) shows up as its URL.
        return prefix + (page && !PAGE_URL.test(page) ? `${page} - ${APP_NAME}` : APP_NAME);
    }

    return prefix + rest.replace(LEADING_APP_NAME, APP_NAME);
}

// Discord sets its own title from the main process after every page change, so the rename has to
// sit on setTitle itself rather than on a page-title-updated listener (Discord's listener runs last).
try {
    const originalSetTitle = BrowserWindow.prototype.setTitle;
    BrowserWindow.prototype.setTitle = function (this: BrowserWindow, title: string) {
        return originalSetTitle.call(this, isEnabled() && typeof title === "string" ? brandTitle(title) : title);
    };
} catch (err) {
    console.error("[Nessacord] Could not patch window titles", err);
}

// ── Taskbar identity ──────────────────────────────────────────────────────────────────────────
// Windows looks up the shortcut that carries a window's AppUserModelID and draws that shortcut's
// icon. Discord's updater writes its stock-icon shortcut back on every launch, so Nessacord's
// windows get their own ID (WINDOW_APP_ID) that only branded taskbar pins carry, which makes
// Windows use the window icon, plus a relaunch icon, name and command so pinning keeps the branding.
function getRelaunchCommand() {
    // <install root>\Discord.exe --processStart Discord.exe, as Discord's own shortcuts do.
    const root = dirname(dirname(process.execPath));
    const launcher = join(root, `${EXE_NAME}.exe`);
    const exe = existsSync(launcher) ? launcher : join(root, "Update.exe");
    return `"${exe}" --processStart ${EXE_NAME}.exe`;
}

// ── Splash (updater) window ───────────────────────────────────────────────────────────────────
// The Nessacord mark in place of Discord's animated logo, with a gentle pulse. The image box is
// 200x200 and the status text overlaps its bottom 30px, so the padding keeps the mark clear of it.
const SPLASH_URL = /\/splash\/index\.html(?:[?#]|$)/;
const SPLASH_CSS = `
.splash-inner img {
    content: url("${MARK_256_DATA_URI}");
    padding: 12px 34px 56px;
    object-fit: contain;
    animation: nessacord-splash-pulse 1.4s ease-in-out infinite alternate;
}

@keyframes nessacord-splash-pulse {
    from { transform: scale(0.94); }
    to { transform: scale(1); }
}

@media (prefers-reduced-motion: reduce) {
    .splash-inner img { animation: none; }
}
`;

function brandSplash(win: BrowserWindow) {
    const { webContents } = win;
    const inject = () => {
        if (win.isDestroyed() || !SPLASH_URL.test(webContents.getURL())) return;
        webContents.insertCSS(SPLASH_CSS).catch(() => { });
    };

    // As soon as the page commits, so the stock logo doesn't flash; again at dom-ready in case
    // the first one landed too early (the rules are identical, so a repeat changes nothing).
    webContents.on("did-navigate", inject);
    webContents.on("dom-ready", inject);

    // The splash takes its window title from its page (<title>Discord Updater</title>) rather than
    // from setTitle, so route that through setTitle, which renames it.
    win.on("page-title-updated", (event, title) => {
        if (!SPLASH_URL.test(webContents.getURL())) return;
        event.preventDefault();
        win.setTitle(title);
    });
}

const branded = new WeakSet<BrowserWindow>();

function brandWindow(win: BrowserWindow) {
    if (branded.has(win) || win.isDestroyed()) return;
    branded.add(win);

    if (!isEnabled()) return;

    brandSplash(win);

    if (process.platform !== "darwin") win.setIcon(getIcon());

    if (process.platform === "win32") {
        try {
            win.setAppDetails({
                appId: WINDOW_APP_ID,
                relaunchCommand: getRelaunchCommand(),
                relaunchDisplayName: APP_NAME,
                ...(hasIconFile && { appIconPath: ICO_PATH, appIconIndex: 0 })
            });
        } catch (err) {
            console.error("[Nessacord] Could not set the taskbar identity", err);
        }
    }

    win.setTitle(win.getTitle());
}

app.on("browser-window-created", (_, win) => brandWindow(win));
app.whenReady().then(() => BrowserWindow.getAllWindows().forEach(brandWindow));

// ── Tray ──────────────────────────────────────────────────────────────────────────────────────
// Discord's core reads its tray images once, at startup, from
// app/images/systemtray/win32/<name>[@<scale>x].png, and picks the scale itself. The Nessacord
// mark is handed over in place of the plain and unread icons. The voice icons (connected,
// speaking, muted, deafened) are plain status glyphs and stay as they are.
const TRAY_IMAGE = /[\\/]systemtray[\\/]win32[\\/]([\w@.-]+\.png)$/;

if (process.platform === "win32") {
    const originalReadFileSync = fs.readFileSync;
    fs.readFileSync = function (this: typeof fs, path: unknown, options?: unknown) {
        if (options == null && typeof path === "string" && path.endsWith(".png")) {
            const name = TRAY_IMAGE.exec(path)?.[1];
            const png = name && TRAY_ICONS[name];
            if (png && isEnabled()) return Buffer.from(png, "base64");
        }
        return originalReadFileSync.apply(this, arguments as any);
    } as typeof fs.readFileSync;
}

try {
    const originalSetToolTip = Tray.prototype.setToolTip;
    Tray.prototype.setToolTip = function (this: Tray, toolTip: string) {
        const text = isEnabled() && typeof toolTip === "string"
            ? toolTip.replace(/Discord(?: (?:Canary|PTB|Development))?/g, APP_NAME)
            : toolTip;
        return originalSetToolTip.call(this, text);
    };
} catch (err) {
    console.error("[Nessacord] Could not patch the tray tooltip", err);
}
