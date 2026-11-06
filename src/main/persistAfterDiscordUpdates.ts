/*
 * Vencord, a modification for Discord's desktop app
 * Copyright (c) 2022 Vendicated and contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

import { app } from "electron";
import EventEmitter from "events";
import { existsSync, readdirSync, renameSync, rmSync, writeFileSync } from "original-fs";
import { basename, dirname, join } from "path";

import { makeInjectorAsar } from "./injector";

function isNewer($new: string, old: string) {
    const newParts = $new.slice(4).split(".").map(Number);
    const oldParts = old.slice(4).split(".").map(Number);

    for (let i = 0; i < oldParts.length; i++) {
        if (newParts[i] > oldParts[i]) return true;
        if (newParts[i] < oldParts[i]) return false;
    }
    return false;
}

function patchLatest() {
    if (process.env.DISABLE_UPDATER_AUTO_PATCHING) return;

    try {
        const currentAppPath = dirname(process.execPath);
        const currentVersion = basename(currentAppPath);
        const discordPath = join(currentAppPath, "..");

        // Only finished installs: Discord's updater stages new versions in ".app-<version>.installing".
        const latestVersion = readdirSync(discordPath).reduce((prev, curr) => {
            return (/^app-\d+(\.\d+)*$/.test(curr) && isNewer(curr, prev))
                ? curr
                : prev;
        }, currentVersion as string);

        if (latestVersion === currentVersion) return;

        const resources = join(discordPath, latestVersion, "resources");
        const newAppAsar = join(resources, "app.asar");
        const newAppAsarBackup = join(resources, "_app.asar");

        if (!existsSync(newAppAsar) || existsSync(newAppAsarBackup)) return;

        console.info(`[Nessacord] Detected Host Update (${currentVersion} -> ${latestVersion}). Repatching...`);

        // Nessacord writes a fresh injector instead of copying the old version's app.asar, so this
        // works even when Discord has already removed the old version's folder.
        const tempAsar = join(resources, "app.asar.nessacord-tmp");
        writeFileSync(tempAsar, makeInjectorAsar(join(__dirname, "patcher.js")));
        try {
            renameSync(newAppAsar, newAppAsarBackup);
        } catch (err) {
            rmSync(tempAsar, { force: true });
            throw err;
        }
        renameSync(tempAsar, newAppAsar);
    } catch (err) {
        console.error("[Nessacord] Failed to repatch latest host update", err);
    }
}

if (process.platform === "win32" || process.platform === "linux") {
    EventEmitter.prototype.emit = new Proxy(EventEmitter.prototype.emit, {
        apply(target, thisArg, argArray) {
            if (argArray[0] === "host-updated") {
                patchLatest();
            }

            return Reflect.apply(target, thisArg, argArray);
        },
    });

    // Try to patch latest on before-quit
    // Discord's Win32 updater will call app.quit() on restart and open new version on will-quit
    app.on("before-quit", patchLatest);
}
