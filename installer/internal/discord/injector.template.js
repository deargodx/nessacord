// Nessacord injector. Written by the Nessacord installer as Discord's resources/app.asar, and
// re-created by Nessacord after Discord updates. Discord's own app.asar is kept as _app.asar.
//
// It loads Nessacord. If Nessacord cannot load, or failed to start several times in a row,
// it starts Discord without Nessacord instead, so Discord always opens.
"use strict";

const fs = require("fs");
const path = require("path");
const { app, dialog } = require("electron");

const PATCHER = __NESSACORD_PATCHER__;
const MAX_FAILED_LAUNCHES = 3;

// Same location as DATA_DIR in Nessacord's src/main/utils/constants.ts.
const DATA_DIR = process.env.VENCORD_USER_DATA_DIR || (process.env.DISCORD_USER_DATA_DIR
    ? path.join(process.env.DISCORD_USER_DATA_DIR, "..", "NessacordData")
    : path.join(app.getPath("userData"), "..", "Nessacord"));
const STATE_FILE = path.join(DATA_DIR, "state.json");

function readState() {
    try {
        return JSON.parse(fs.readFileSync(STATE_FILE, "utf8"));
    } catch {
        return {};
    }
}

function writeState(state) {
    try {
        fs.mkdirSync(DATA_DIR, { recursive: true });
        fs.writeFileSync(STATE_FILE, JSON.stringify(state));
    } catch { }
}

function patcherStamp() {
    try {
        return String(fs.statSync(PATCHER).mtimeMs);
    } catch {
        return "missing";
    }
}

function startDiscordWithoutNessacord(reason) {
    console.error("[Nessacord] Starting Discord without Nessacord:", reason);
    const asar = path.join(__dirname, "..", "_app.asar");
    const pkg = require(path.join(asar, "package.json"));
    app.setAppPath(asar);
    require.main.filename = path.join(asar, pkg.main);
    require(require.main.filename);
}

const state = readState();
const failed = Number(state.failedLaunches) || 0;

// --vanilla starts Discord without Nessacord on purpose (handled by Nessacord's patcher), so it
// is not counted as a failed launch.
if (process.argv.includes("--vanilla")) {
    require(PATCHER);
} else if (failed >= MAX_FAILED_LAUNCHES && state.safeModeStamp === patcherStamp()) {
    // Safe mode lasts until Nessacord's files change (a repair, reinstall or update), then it tries again.
    if (!state.safeModeNotified) {
        writeState({ ...state, safeModeNotified: true });
        app.whenReady().then(() => dialog.showMessageBox({
            type: "warning",
            title: "Nessacord",
            message: "Nessacord did not start properly",
            detail: `Nessacord failed to start ${failed} times in a row, so Discord was opened without it.\n\nRun the Nessacord installer and choose Repair to fix it.`
        })).catch(() => { });
    }
    startDiscordWithoutNessacord(`it failed to start ${failed} times in a row`);
} else {
    writeState({
        failedLaunches: failed >= MAX_FAILED_LAUNCHES ? 1 : failed + 1,
        safeModeStamp: patcherStamp()
    });

    try {
        require(PATCHER);
    } catch (err) {
        // Once Nessacord has handed over to Discord, an error is Discord's own: don't start it twice.
        if (global.__nessacordDiscordLoading) throw err;
        startDiscordWithoutNessacord(err);
    }
}
