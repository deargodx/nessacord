/*
 * Vencord, a Discord client mod
 * Copyright (c) 2026 Vendicated and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

// Builds dist/NessacordInstaller.exe with the C# compiler that ships with Windows (.NET Framework 4),
// so nothing has to be downloaded. Windows only.

import { execFileSync } from "child_process";
import { existsSync, mkdirSync, readdirSync } from "fs";
import { join } from "path";

if (process.platform !== "win32") {
    console.error("The installer can only be built on Windows.");
    process.exit(1);
}

const framework = join(process.env.WINDIR ?? "C:\\Windows", "Microsoft.NET", "Framework64", "v4.0.30319");
const csc = join(framework, "csc.exe");
if (!existsSync(csc)) {
    console.error(`No C# compiler at ${csc}`);
    process.exit(1);
}

const src = join("installer", "src");
const out = join("dist", "NessacordInstaller.exe");
mkdirSync("dist", { recursive: true });

const sources = readdirSync(src).filter(f => f.endsWith(".cs")).map(f => join(src, f));
const resources = {
    "injector.template.js": join("installer", "internal", "discord", "injector.template.js"),
    "icon.ico": join("installer", "build", "windows", "icon.ico"),
    "appicon.png": join("installer", "build", "appicon.png")
};

execFileSync(csc, [
    "-nologo",
    "-target:winexe",
    "-optimize+",
    "-platform:anycpu",
    `-out:${out}`,
    `-win32icon:${resources["icon.ico"]}`,
    `-win32manifest:${join(src, "app.manifest")}`,
    ...["System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll", "System.Web.Extensions.dll"].map(r => `-reference:${r}`),
    ...Object.entries(resources).map(([name, path]) => `-resource:${path},${name}`),
    ...sources
], { stdio: "inherit" });

console.log(`Built ${out}`);
