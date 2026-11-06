/*
 * Vencord, a Discord client mod
 * Copyright (c) 2026 Vendicated and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

// Nessacord's injector: the small app.asar that replaces Discord's own (kept as _app.asar) and
// loads Nessacord. The installer writes the same file from the same template.

import injectorTemplate from "file://../../installer/internal/discord/injector.template.js";
import { writeFileSync } from "original-fs";
import { join } from "path";

import { DATA_DIR } from "./utils/constants";

export const STATE_FILE = join(DATA_DIR, "state.json");

/** The injector's index.js, loading the patcher at `patcherPath`. */
export function makeInjectorSource(patcherPath: string) {
    return injectorTemplate.replace("__NESSACORD_PATCHER__", () => JSON.stringify(patcherPath)) + "\n";
}

/** Packs files into an asar archive (Electron's format: a pickled JSON header, then the file bytes). */
export function makeAsar(files: Record<string, string>) {
    const contents = Object.values(files).map(c => Buffer.from(c, "utf8"));

    let offset = 0;
    const header = { files: {} as Record<string, { size: number; offset: string; }> };
    Object.keys(files).forEach((name, i) => {
        header.files[name] = { size: contents[i].length, offset: String(offset) };
        offset += contents[i].length;
    });

    const json = Buffer.from(JSON.stringify(header), "utf8");
    const padding = (4 - json.length % 4) % 4;

    const headerPickle = Buffer.alloc(8 + json.length + padding);
    headerPickle.writeUInt32LE(4 + json.length + padding, 0);
    headerPickle.writeUInt32LE(json.length, 4);
    json.copy(headerPickle, 8);

    const sizePickle = Buffer.alloc(8);
    sizePickle.writeUInt32LE(4, 0);
    sizePickle.writeUInt32LE(headerPickle.length, 4);

    return Buffer.concat([sizePickle, headerPickle, ...contents]);
}

export function makeInjectorAsar(patcherPath: string) {
    return makeAsar({
        "index.js": makeInjectorSource(patcherPath),
        "package.json": JSON.stringify({ name: "discord", main: "index.js" }, null, "\t")
    });
}

/** Called once Discord has been running with Nessacord for a while: resets the injector's failed-launch count. */
export function markLaunchHealthy() {
    try {
        writeFileSync(STATE_FILE, JSON.stringify({ failedLaunches: 0 }));
    } catch { }
}
