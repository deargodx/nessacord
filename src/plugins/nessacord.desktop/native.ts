/*
 * Vencord, a Discord client mod
 * Copyright (c) 2026 deargod and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

// Nessacord, main-process side: window/taskbar icon, window title, tray tooltip and splash logo,
// plus (opt-in) the Start menu shortcuts. Based on deargod's GodCord.
//
// Nothing is exported on purpose: every export of a native.ts is registered as an IPC handler.
// Discord's userData path is never touched (no app.setName), so logins and settings stay put.

import { RendererSettings } from "@main/settings";
import { DATA_DIR } from "@main/utils/constants";
import { app, BrowserWindow, NativeImage, nativeImage, shell, Tray } from "electron";
import { existsSync, mkdirSync, readdirSync, readFileSync, renameSync, rmSync, watch, writeFileSync } from "fs";
import { basename, dirname, join } from "path";

import { ICON_ICO_BASE64, MARK_256_DATA_URI } from "./iconData";

const APP_NAME = "Nessacord";
const ICO_PATH = join(DATA_DIR, "nessacord.ico");

function pluginSettings() {
    return RendererSettings.store.plugins?.Nessacord;
}

function isEnabled() {
    // enabledByDefault: on the very first launch the plugin has no settings entry yet.
    return pluginSettings()?.enabled !== false;
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

// ── Start menu shortcuts (opt-in) ─────────────────────────────────────────────────────────────
// Start menu, search and notification headers use the shortcut that carries Discord's
// AppUserModelID. Discord's updater re-creates its own shortcut with the stock icon a few seconds
// into every launch, so when enabled the shortcuts are re-branded at startup and whenever one
// re-appears: re-iconed, renamed to Nessacord.lnk, and a re-created duplicate is removed.
const PROGRAMS_DIR = join(app.getPath("appData"), "Microsoft", "Windows", "Start Menu", "Programs");
const SHORTCUT_DIRS = [PROGRAMS_DIR, join(PROGRAMS_DIR, "Discord Inc")];

// Discord sets com.squirrel.<Name>.<Name> as its AppUserModelID (e.g. com.squirrel.Discord.Discord).
const EXE_NAME = basename(process.execPath, ".exe");
const APP_ID = `com.squirrel.${EXE_NAME}.${EXE_NAME}`;

function isOurShortcut(path: string) {
    try {
        return shell.readShortcutLink(path).appUserModelId === APP_ID;
    } catch {
        return false;
    }
}

function brandStartMenuShortcuts() {
    if (process.platform !== "win32" || !hasIconFile || !isEnabled() || !pluginSettings()?.brandShortcuts) return;

    for (const dir of SHORTCUT_DIRS) {
        let names: string[];
        try {
            names = readdirSync(dir).filter(n => /\.lnk$/i.test(n) && /discord|godcord|nessacord/i.test(n));
        } catch {
            continue;
        }

        const brandedPath = join(dir, `${APP_NAME}.lnk`);
        for (const name of names) {
            const path = join(dir, name);
            if (!isOurShortcut(path)) continue;

            const link = shell.readShortcutLink(path);
            if (link.icon !== ICO_PATH || link.description !== APP_NAME)
                shell.writeShortcutLink(path, "update", { target: link.target, icon: ICO_PATH, iconIndex: 0, description: APP_NAME });

            if (path.toLowerCase() === brandedPath.toLowerCase()) continue;

            if (!existsSync(brandedPath)) renameSync(path, brandedPath);
            else if (isOurShortcut(brandedPath)) rmSync(path);
        }
    }
}

function tryBrandStartMenuShortcuts() {
    try {
        brandStartMenuShortcuts();
    } catch (err) {
        console.error("[Nessacord] Could not re-brand the Start menu shortcuts", err);
    }
}

if (process.platform === "win32") {
    // At startup, before Discord's first window exists, so its taskbar button resolves to the branded shortcut.
    tryBrandStartMenuShortcuts();

    // Then whenever Discord's updater writes its own shortcut back. Debounced so the updater has
    // finished writing the file before it is read; the pass is idempotent.
    let timer: ReturnType<typeof setTimeout> | undefined;
    const onChange = (_event: string, file: string | Buffer | null) => {
        if (file && !/discord.*\.lnk$/i.test(String(file))) return;
        clearTimeout(timer);
        timer = setTimeout(tryBrandStartMenuShortcuts, 1000);
    };

    for (const dir of SHORTCUT_DIRS) {
        try {
            watch(dir, { persistent: false }, onChange).on("error", () => { });
        } catch {
            // Folder missing: nothing to keep branded there.
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

function brandTitle(raw: string) {
    const prefix = UNREAD_PREFIX.exec(raw)?.[0] ?? "";
    const rest = raw.slice(prefix.length).trim();

    if (!rest || WHOLE_APP_NAME.test(rest)) return prefix + APP_NAME;

    if (TRAILING_APP_NAME.test(rest)) {
        const page = rest.replace(TRAILING_APP_NAME, "").trim();
        return prefix + (page ? `${page} - ${APP_NAME}` : APP_NAME);
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
// windows get their own ID that no shortcut carries, which makes Windows use the window icon, plus
// a relaunch icon, name and command so pinning keeps the branding.
const WINDOW_APP_ID = `${APP_NAME}.${EXE_NAME}`;

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
// Tooltip only. The tray icon is left alone so Discord's unread, mute and voice status icons keep working.
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
