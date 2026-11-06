# Nessacord: Build Plan

A third-party Discord desktop client mod, built on the GodCord / vencord-lean base, that looks like the inspiration render (`maininspiration.png`), ships as an easy `.exe`, is fully optimised, and keeps working through Discord's automatic updates.

Status: **planning only. Nothing has been built, installed, committed or pushed.** Everything below comes from reading the files listed in section 2, sampling the inspiration image, and reading the local Discord and Vencord installs (read-only).

---

## 0. Summary

**What Nessacord is:** a Vencord-based client mod for Windows. It patches the user's local Discord install (it does not redistribute Discord's files), applies a black-and-silver visual theme with brand, layout and font changes, and adds a Nessacord settings tab. It ships as a GitHub release with an installer and a portable patcher.

**Recommendation in one paragraph:** Make Nessacord a **distribution of vencord-lean**, not a loose set of userplugins. The rebrand touches the injector, the updater source, the data directory and the window chrome, and those need core changes. Keep the visual layer as a normal plugin so it stays modular. Write a small **Go installer** that does what `pnpm inject` does today, but for Nessacord's own build and paths. Add a **repair layer** so Discord's updater cannot silently remove the patch. Put a **budget** on size, memory and startup time, and check it on every release.

**Decisions I need from you** (full list in section 13):
1. GitHub owner/org for the repo, and whether to keep this as a public repo.
2. What **bella** should be credited for (I only have the name).
3. Photo and name rights. The photo is of a real person and the name is derived from hers. My recommendation is not to bundle the photo in the public release.
4. Whether this machine's Discord Stable and Canary (both currently Vencord-patched) should be switched to Nessacord at the first test.

---

## 1. Scope and interpretation

Your brief has a few phrases I have read in a specific way. Tell me if any is wrong.

| Your words | How I read it | Done when |
|---|---|---|
| "Look just as good as the inspiration" | Matches palette, typography, layout, backgrounds and brand at 1672×941 by overlay, with no obvious regressions at 1920×1080 and 1366×768 | Overlay diff and checklist in 3.8 pass |
| "Use the optimised Vencord (GodCord) CLI" | Use vencord-lean as the engine and its build pipeline (esbuild, the patch system, the plugin API). Its `pnpm inject` path is the reference for the installer | Nessacord builds from the vencord-lean base with no regressions in its own test suite |
| "Support Vencord" | (a) Nessacord stays Vencord-compatible, so Vencord plugins and themes keep working. (b) It can replace or sit alongside a Vencord install without corrupting it | Vencord themes load unchanged; Vencord → Nessacord migration works on a test install |
| "Support Discord's automatic updates without breaking" | After any Discord update, Nessacord is re-applied automatically, or the user gets one click to repair, with no data loss | Tested by simulating a Discord version bump (section 9) |
| "Fully optimised" | Measured budgets for startup, memory, size and zero runtime network calls from the theme | Budgets in section 7 met on each release |
| "Easy to access .exe / installer / patcher" | One `Nessacord-Setup-x.y.z.exe` for normal users and one `Nessacord-Patcher-x.y.z.exe` (portable, same code) | Both attached to a GitHub release with SHA256 sums |

---

## 2. What I found (evidence)

### 2.1 The base project: `C:\Users\spicy\dev\vencord-lean`
- Vencord **1.15.16** fork at commit `383409e6` on branch `main`. Remote `origin` is `Vendicated/Vencord` (upstream).
- **170** built-in plugin folders under `src/plugins`.
- **Uncommitted work:** 18 modified files and 1 untracked file (`src/webpack/patchMatcher.ts`). The largest change is `src/webpack/patchWebpack.ts` (+211/−13). This is the "optimised" part of the CLI. Its code comment claims about 5× faster patch scanning and says it was "verified against the plain includes() loop on every module". **That claim has not been measured by me.** It must be verified (M0) before Nessacord builds on it.
- **GodCord is a userplugin:** `src/userplugins/godcord.desktop/` (`index.ts`, `native.ts`, `godcord.css`, `make-assets.py`, icon assets). It rebrands Discord's text, logos, title bar, splash, taskbar and tray. Author field: `deargod`.
- **ToughLoveTheme** (`src/userplugins/toughLoveTheme/`) is a crimson theme with its CSS embedded. Author: `deargod`.
- **Important:** `.gitignore` line 22 ignores `src/userplugins`. **GodCord, ToughLove and nsfwBypass are not in git.** They exist only on disk. Nessacord must move them into tracked source (M0).
- `scripts/runInstaller.mjs` (`pnpm inject`) downloads Vencord's upstream `VencordInstallerCli.exe` from the Vencord/Installer releases and runs it with `VENCORD_DEV_INSTALL=1`. This is the "CLI" you mean.
- `dist/` is already built (`renderer.js` ≈ 815 KB, `vencordDesktopRenderer.js` ≈ 825 KB, `patcher.js` ≈ 109 KB, `vencordDesktopMain.js` ≈ 105 KB, and so on).
- Toolchain on this machine: Node v22.16.0, pnpm 11.5.0 (`C:\Users\spicy\AppData\Local\pnpm\bin\pnpm.ps1`). GitHub CLI (`gh`) is **not installed**.

### 2.2 How Vencord patches Discord (verified in the code and in a real install)
- Discord's install is `%LOCALAPPDATA%\Discord\` (Stable) or `%LOCALAPPDATA%\DiscordCanary\` (Canary). Each has `Update.exe`, the launcher `.exe`, and `app-<version>\resources\app.asar`.
- Vencord **renames** Discord's original `app.asar` to `_app.asar` and writes a tiny **injector** as `app.asar`.
- I read the injector in the live Stable install (`app-1.0.9267`). It is 219 bytes:
  - `index.js`: `require("C:\Users\spicy\AppData\Roaming\Vencord\dist\patcher.js")`
  - `package.json`: `{"name": "discord", "main": "index.js"}`
- `src/main/patcher.ts` loads the original `_app.asar` and then Vencord's `patcher.js`. The asar name logic is `require.main.path.endsWith("app.asar") ? "_app.asar" : "app.asar"`.
- `src/main/persistAfterDiscordUpdates.ts` handles Discord updates. On `host-updated` or `before-quit`, it finds a newer `app-*` folder, **copies the injector from the old folder's `app.asar`** into the new folder's `app.asar`, and renames the new stock asar to `_app.asar`. It only runs if the old folder still exists.
- The data directory defaults to `%APPDATA%\Vencord` (`src/main/utils/constants.ts`). Overridable with `VENCORD_USER_DATA_DIR`.

### 2.3 The current machine (read-only check)
| Channel | Folder | State |
|---|---|---|
| Stable | `%LOCALAPPDATA%\Discord\app-1.0.9267\resources\` | `_app.asar` + injector `app.asar` → **Vencord-patched** |
| Canary | `%LOCALAPPDATA%\DiscordCanary\app-1.0.1313\` and `app-1.0.1315\` | Both patched. Also `.app-1.0.1316.installing` (an update in progress) |

- Vencord data is in `%APPDATA%\Vencord\` (`dist\`, `settings\settings.json`, `themes\`). The Vencord settings show `autoUpdate: true`, 44 enabled plugins, and `enabledThemes: ["toughlove.css"]`.
- Existing themes in `%APPDATA%\Vencord\themes\`: `nessatheme.css` (a grey, near-black palette, close to the target), `toughlove.css`, `Novum.theme.css`, and two others. Some load images from external hosts (`i.ibb.co`, `przemec.github.io`). Public Nessacord themes must not do this.
- A GodCord-era backup of the settings exists (`settings.json.bak-godcord-…`), so there is already a rollback point.

### 2.4 Updater source (matters for "without breaking")
- `src/main/updater/` takes its repo from the build-time define `~git-remote` (`scripts/build/common.mjs`). It uses the `VENCORD_REMOTE` env var, or `git remote get-url origin`.
- `origin` is `Vendicated/Vencord`. **A Nessacord build made without setting `VENCORD_REMOTE` would check upstream Vencord for updates** and could replace Nessacord with stock Vencord. `IS_STANDALONE` picks the HTTP updater (release assets) or the git updater (dev builds).

### 2.5 Rebrand scope
- 479 files in `src/` and 11 in `scripts/` mention "Vencord". Most are attribution and license headers, which must stay. The user-visible strings are a smaller set.
- Discord's text is in intl message tables. The GodCord patch (`all: true`) rewrites literal text there. It deliberately leaves the all-caps `DISCORD` alone (it is the Crisis Text Line keyword).

### 2.6 The inspiration images
- `maininspiration.png` (1672×941, RGB): the target UI. It is an **AI-generated mock-up**, not a real Discord screenshot. It shows no server bar, which I cover in section 3.7.
- `inspiration1.jpg` (736×1274, RGB): **lace texture**. Portrait, not seamless.
- `inspiration2.jpg` (750×750, RGB): **black-and-white photograph** of a performer at a vintage microphone. Used in the render as the chat-side image and an embed.
- `nessacordicon.png` (1282×1227, RGBA): the **vampire-mouth icon**, white outline on a black fill, about 51% transparent. Content bounding box (25,147)–(1235,1148). Works on the dark background without a backing plate.

### 2.7 Name check
A quick web search for "Nessacord Discord client mod" found **no existing project** with this name. That is not a trademark clearance. Do a proper check before the public release (GitHub, npm, a trademark search).

---

## 3. Target design

### 3.1 Method
- The render is the visual target. I sampled colours from it, measured the main edges by eye at the 1672 px width, and read the typography from the glyphs.
- All measurements are **approximate (±3 px at 1672 px)**. The build step must check them with a side-by-side overlay (section 9.4) and not trust my numbers blindly.
- Where the render is internally inconsistent, I say so and propose a decision (3.7 and 13).

### 3.2 Layout map (approximate, from the 1672×941 render)

```
+--------------------------------------------------------------------------------+
| 18px                                                                           |
|  +----------------+  +-----------------------------------+  +----------------+  |
|  | BRAND  (68px)  |  | # general  topic      icons  srch |  | ONLINE — 6     |  |
|  |----------------|  |  (68px header, hairline below)    |  |                |  |
|  | Friends        |  |                                   |  | [photo of     |  |
|  | Nitro          |  |  messages (avatar 56px, text 19)  |  |  performer    |  |
|  | Shop           |  |                                   |  |  at right of  |  |
|  | DIRECT MSGS    |  |                                   |  |  chat panel]  |  |
|  | DM rows 44px   |  |                                   |  |                |  |
|  |                |  |                                   |  | member rows    |  |
|  |                |  |  [ + | Message #general...  🎁 GIF ☺ ✎ ]|  | 50–56px       |  |
|  | user panel     |  +-----------------------------------+  |                |  |
|  +----------------+        input bar 55px tall             +----------------+  |
|  295px                    chat panel ~1125px                  ~213px          |
+--------------------------------------------------------------------------------+
```

Starting metrics (to tune):
- Outer window padding: 18 px sides, 20 px top and bottom.
- Left column: about 295 px (x 18 → 312).
- Chat panel: about 1125 px (x 315 → 1440). Header 68 px (y 20 → 88) with a hairline underneath. Input bar 55 px tall, 30 px inset from the panel sides, about 10 px corner radius.
- Member column: about 213 px (x 1442 → 1655).
- Panel corner radius about 8 px, 1 px borders in `#1a1a1a` to `#2a2a2a`.
- DM row pitch about 44 px, DM avatar 40 px. Member row pitch 50–56 px (offline rows a little tighter), member avatar 40 px. Message avatar 56 px.

### 3.3 Palette (sampled from the render, then proposed)

Sampled values come from the render. Proposed values are marked **(proposed)**.

| Role | Value | Source | Discord variable(s) to set |
|---|---|---|---|
| App frame and lace base | `#000000` | sampled ≈ `#060605` | `--background-tertiary` |
| DM / channel sidebar | `#020201` over lace at about 10% | sampled `#020201`, `#070706` | `--background-secondary`, `--background-secondary-alt` |
| Chat panel | `#020202` | sampled `#020202` | `--background-primary` |
| Message input | `#050505`, 1 px border `#2a2a2a` | sampled `#050505` | `--channeltextarea-background` |
| Member column | `#010101` | sampled `#010101` | `--background-primary-alt` (name to confirm) |
| Popouts, menus, modals | `#0a0a0a`, 1 px `#222222` **(proposed)** | — | `--background-floating`, `--background-nested-floating` |
| Hover | `rgba(255,255,255,0.04)` **(proposed)** | — | `--background-modifier-hover` |
| Active / selected | `rgba(255,255,255,0.08)` **(proposed)** | — | `--background-modifier-active`, `--background-modifier-selected` |
| Primary text | `#f1f0ee` | sampled heading `#f1f0ee` | `--text-normal`, `--header-primary` |
| Bright text (messages) | `#ffffff` | sampled `#ffffff` | `--header-primary` (where used) |
| Secondary text | `#d6d5d7` | sampled `#d6d5d7` | `--header-secondary` |
| Muted text (timestamps, hints) | `#8c8c8c` **(proposed)** | — | `--text-muted` |
| Offline names | `#5c5c5c` **(proposed)** | render: dim grey, eyeballed, not sampled | `--channel-icon`, offline member text |
| Online status dot | `#478a6a` | estimated from the status dot (small sample) | status colour variables (names to confirm in Discord's CSS) |
| Dividers, hairlines | `#1a1a1a` **(proposed)** | — | `--border-faint` |
| Strong borders | `#2a2a2a` **(proposed)** | — | `--border-strong` |
| Crown (owner) | muted gold (sample the exact value in M2) | visible in render | role/crown icon colour |

Notes:
- **Contrast.** Approximate WCAG contrast on `#020202`: `#8c8c8c` ≈ 6:1 (passes AA for small text). `#686868`, the value in the existing `nessatheme.css`, is ≈ 3.7:1 (fails AA for small text). The render's offline names look dim, close to 3:1 by eye. I propose `#5c5c5c` (≈ 3:1 on black), which keeps them dim but readable. This is a deliberate small departure from the render, and your call.
- **Monochrome on purpose.** The render's only colour is the green status dot and the gold crown. Nessacord should not add an accent colour. The theme is dark-only: the vampire-mouth icon's white outline disappears on a light background, so the light theme is disabled.
- **Starting point.** `nessatheme.css` already uses greys close to this. Reuse its variable names, but not its external images.

### 3.4 Typography
- **Family:** Cormorant Garamond (SIL Open Font License). Weights 400, 500, 600, plus italic 400 and 500. Matches the render's high-contrast serif and the italic usernames and topic.
- **Uppercase labels** ("DIRECT MESSAGES", "ONLINE — 6", "OFFLINE — 4"): Cormorant Garamond, uppercase, 12–13 px, letter-spacing 0.12em, colour `--text-muted`.
- **Starting sizes** (tune at 100% scale on a 1080p and a 1440p screen):

| Element | Style | Size |
|---|---|---|
| Brand wordmark "Nessacord" | 500 | 26 px |
| Channel name "general" | 500 italic | 26 px |
| Channel topic "talk about anything ♡" | italic | 16 px, muted |
| Message username | italic 500 | 19 px |
| Message timestamp | 400 | 13 px, muted |
| Message body | 400 | 19 px |
| DM / member names | 400 (online) / 400 dimmed (offline), italic for the active user | 18 px |
| Input placeholder | italic | 18 px |
| Buttons and small UI | 500 | 15–16 px |

- Cormorant has a small x-height, so the body is bigger than Discord's default. Check line wrapping on long messages, the settings UI and the modals.
- **Delivery:** Latin-subset `woff2` files, about 40–60 KB each, embedded in the theme as `@font-face` data URIs. Remote Google Fonts are rejected (privacy, CSP, reliability). Check first that Discord's CSP allows `font-src data:`. If not, use the CSP manager in `src/main/csp/`, which already allows the `vencord:` scheme.
- **Fallbacks:** `"Cormorant Garamond", "Garamond", "Georgia", serif`.

### 3.5 Backgrounds and imagery

**Lace (`inspiration1.jpg`)**
- Turn it into a **seamless tile** (offset-and-blend, or mirrored) at about 736 px, greyscale, levels adjusted to near black. Target size about 40–70 KB as WebP.
- Apply it to the app root and the left column at about 8–12% visibility, so it reads through the black. It must not sit behind text. The chat panel stays solid.
- Rights: source and licence of the lace texture are unknown to me. Confirm you can ship it (M2).

**Photo (`inspiration2.jpg`)**
- Render usage: (a) a large portrait image filling the right side of the chat panel behind the messages, fading to black on its left edge; (b) an image embed inside the chat with a thin border and small radius.
- Source is 750 px square. The right-hand panel is roughly 350×830 px in the render, so it is upscaled, and will look soft on a 1440p screen. Ask for a **2000 px or larger** source if you want it crisp.
- **Recommendation:** do not bundle the photo in the public release. Ship the lace-only default. Add a Nessacord setting "Background image" where the user picks their own file, stored locally in `%APPDATA%\Nessacord\assets\`. Your own install keeps the render's look. (Section 8 explains why.)

**Avatars**
- Greyscale for every avatar and member avatar (`filter: grayscale(1)` with a little contrast). This reproduces the render. Hover or a user setting can restore colour.

**Scrollbars:** thin, `#2e2e2e` thumb on transparent track (same as the existing `nessatheme.css`).

### 3.6 Iconography and brand

**Brand (replaces the Discord logo and "Discord" text top-left)**
- Mark: `nessacordicon.png`. Produce the following from the source:
  - 512 px PNG for the in-app header and the About page.
  - Multi-size `.ico` (16, 20, 24, 32, 40, 48, 64, 96, 128, 256) for the window, taskbar and shortcuts.
  - 96 px WebP for the home button (2× the display size), as GodCord does.
  - Tray icon at 16/32 px.
- Wordmark: "Nessacord" in Cormorant Garamond 500, 26 px, to the right of the mark. Keep the small dropdown chevron the render shows.
- Generalise GodCord's `make-assets.py` (crop to bounding box, pad to a square, write `.ico`, a `iconData.ts`, and CSS data URIs) into `tools/make-assets.py`.

**Icons**
- The render uses thin 1.5 px line icons for the top bar (bell, pin, add-friend, search, help), the input bar (plus in a circle, gift, GIF, emoji, sticker), and the sidebar (friends, Nitro, shop, mic, headphones, settings).
- Approach:
  - Where Discord's icon is a stroked SVG, set the stroke width in CSS.
  - Where it is a filled glyph, replace the SVG through the plugin's DOM hooks. Use a line set: Lucide (ISC licence) or Phosphor "Light" (MIT). Keep licence files in the repo.
- Icon swaps are the most fragile part (Discord changes them). Budget for them in the health checks (section 6.4).

### 3.7 Component-by-component spec

| Region | Discord element | Nessacord treatment | Method |
|---|---|---|---|
| Top-left header | Server/home header with logo and dropdown | Nessacord mark + wordmark + chevron, 68 px tall | Plugin DOM swap + CSS |
| Sidebar nav | Friends, Nitro, Shop | Thin icons, Cormorant labels, 44 px rows | CSS + icon swap |
| DM section header | "DIRECT MESSAGES" + "+" | Uppercase tracked label, `+` as thin icon | CSS |
| DM rows | Avatar + name + status | 40 px avatar greyscale, name in Cormorant, status line muted italic | CSS |
| User panel (bottom-left) | Avatar, name, mic, headphones, settings | Same spacing, icons thinned | CSS + icon swap |
| Chat header | `#` channel name, topic, icon cluster, search | 68 px, italic channel name, topic muted italic, search as a rounded outline field | CSS + icon swap |
| Messages | Avatars, usernames, timestamps, text, embeds | Serif text; greyscale avatars; image embed with 1 px border and small radius | CSS |
| Chat background | Plain | Photo panel (user-supplied, optional) fading to black; lace behind everything | CSS background on the chat container |
| Input bar | Field with icons | 55 px, rounded, 1 px border, italic placeholder, thin icons | CSS + icon swap |
| Member list | "ONLINE — n" / "OFFLINE — n" | Uppercase tracked labels; online names Cormorant, offline dimmed | CSS |
| Popouts, menus, modals | Discord surfaces | `--background-floating` and borders from 3.3; serif headings | CSS variables |
| Settings (Discord and Vencord) | Discord settings | Same palette and fonts | CSS variables |
| Loading / splash | Animated Discord logo | Nessacord mark with a gentle pulse, respecting reduce-motion | GodCord's approach |
| Title bar (Windows legacy) | Discord wordmark | "Nessacord" text | GodCord CSS |
| Window title, taskbar, tray | "Discord" | "Nessacord" | GodCord's `native.ts` approach |

**The server bar:** the render shows no server list on the far left, which real Discord always has. I propose **keeping it** and styling it in black, with a 1 px divider and the same icon treatment. Removing it would break navigation. Say if you want it hidden. (Decision in section 13.)

**Nitro and Shop labels:** they are functional product names, not branding. I propose keeping them. The "Discord" → "Nessacord" text rewrite stops at UI chrome (section 4.3).

### 3.8 Acceptance checklist (the "looks just as good" test)

- [ ] Overlay diff at 1672×941 against `maininspiration.png`: average difference below a threshold agreed in M2, and no region missing.
- [ ] Palette values match section 3.3 (checked by sampling the running app, same as section 3.3).
- [ ] Layout metrics within ±4 px of section 3.2 at 1672×941.
- [ ] Serif type everywhere Discord shows text in the chat, sidebar and member list, with no Gotham/gg-sans leftovers.
- [ ] Brand mark and wordmark top-left, no Discord logo visible anywhere in the main UI.
- [ ] Lace visible but not behind text; photo area (if a user image is set) fades to black.
- [ ] Greyscale avatars, online dots green, offline names dimmed.
- [ ] Check at 1920×1080 and 1366×768: no overflow, member list hides below the width threshold as Discord does.
- [ ] Popouts, modals, context menus, tooltips and the settings UI all use the palette.
- [ ] Contrast of body and muted text meets WCAG AA for small text (4.5:1), except where the user chooses otherwise.

---

## 4. Architecture

### 4.1 Repository layout (proposed)

Repo root: **`C:\nessacord`** (already the working folder, currently holding the assets). The code is imported from vencord-lean with history (`git init`, fetch from `C:\Users\spicy\dev\vencord-lean`, then reset to the commit). vencord-lean itself is not modified (section 4.2).

```
C:\nessacord\                       (repo: nessacord)
├── src/                            from vencord-lean (core, plugins, webpack)
│   └── plugins/
│       ├── nessacord/              brand + text rebrand + native (promoted from godcord.desktop)
│       └── nessacordTheme/         visual layer (CSS, fonts, assets, settings tab)
├── src/main/persistAfterDiscordUpdates.ts   changed: writes the injector instead of copying it (section 6)
├── scripts/build/common.mjs        changed: VENCORD_REMOTE default to Nessacord's repo
├── themes/nessacord/               source CSS (variables, component rules), fonts/, lace.webp, README
├── assets/brand/                   nessacordicon.png (source), SVG if drawn, generated ico/png/webp
├── tools/make-assets.py            generalised from GodCord's make-assets.py
├── installer/                      Go module: patcher CLI + Wails GUI (section 5)
├── docs/                           install, troubleshooting, patch-health runbook, architecture
├── .github/workflows/
│   ├── build.yml                   from upstream, adapted (dev + standalone builds)
│   ├── release.yml                 new: builds installer + portable + zip, SHA256SUMS, attestations
│   └── patch-health.yml            new: runs the find/match checks against stored snapshots (section 6.4)
├── LICENSE                         GPL-3.0-or-later, untouched
├── NOTICE                          new: Vencord attribution, GodCord/Nessacord credits, third-party licences
├── CREDITS.md                      deargod, bella, Vencord contributors, font and icon licences
└── README.md                       install, uninstall, disclaimer, troubleshooting
```

Things I deliberately **do not** rename:
- Internal identifiers: the `Vencord` symbols, the `vencord://` protocol and CSP entries, and the `Vencord` plugin API names. Renaming them would make every upstream merge painful. Only user-visible strings, paths and the product name change.
- The licence headers in source files. They must stay (GPL).

### 4.2 Base and sync strategy
- Nessacord tracks **upstream Vencord by tag**, not by `main`. Start from `v1.15.16`, plus the vencord-lean changes after M0 verification (committed as their own commits, so they can be dropped if they regress).
- Keep Nessacord's own changes in **separate, named commits** (`nessacord:` prefix), so upstream rebases are reviewable.
- Sync cadence: every upstream minor release or when Discord breaks something, whichever is first. Each sync has a changelog entry and a full test run.
- vencord-lean stays as it is, your source of truth for GodCord.

### 4.3 Runtime components

**(a) Branding and text (`src/plugins/nessacord`)**
- Text rewrite of UI chrome, using GodCord's intl-table patch (`all: true`) as the base.
- Explicit **exclusions**: all-caps `DISCORD` (Crisis Text Line keyword), `Discord Inc.`, legal, privacy and policy text, URLs, and product names where the wording is a legal identifier. These are listed in a single file, so they are auditable.
- Window title, taskbar identity, tray tooltip, splash (from GodCord's `native.ts`). These are kept, but moved into a clean, documented module.
- Start-menu and taskbar shortcut re-branding: **opt-in** in Nessacord (GodCord has it on by default). It writes to the user's Start Menu, so it must be disclosed and removed on uninstall.

**(b) Visual theme (`src/plugins/nessacordTheme`)**
- A plugin that loads `themes/nessacord/*.css`, the fonts and images as embedded data or `vencord:`-scheme assets.
- CSS variables (section 3.3) first. Targeted selectors only where variables cannot reach.
- Settings: Nessacord tab in Vencord settings with the wallpaper picker, photo on/off, greyscale avatars on/off, brand text on/off, reduce-motion override, and a health panel (section 6.4).

**(c) Core changes (small, reviewable)**
- `src/main/utils/constants.ts`: default data dir `%APPDATA%\Nessacord` (overridable by `VENCORD_USER_DATA_DIR`), so it never collides with a Vencord install.
- `src/main/persistAfterDiscordUpdates.ts`: generate the injector instead of copying it (section 6.2).
- `src/main/patcher.ts`: fail-open loading (section 6.3).
- `scripts/build/common.mjs`: `VENCORD_REMOTE` default `<owner>/nessacord` for the updater.
- `src/main/updater/*`: verify SHA256 sums from the release before applying an update.
- Cloud settings sync (`src/components/settings/tabs/sync/CloudTab.tsx`, `cloud` in `settings.json`) uses Vencord-operated services. **Default off and removed from the UI** in Nessacord, with no outbound calls.

### 4.4 Build pipeline and release variants
- `pnpm build --dev` for local iteration (vencord-lean's rule: always `--dev` for verification).
- `pnpm build --standalone` for release. This selects the HTTP updater and release assets rather than the git updater. It needs `VENCORD_REMOTE` set.
- Release-only: strip source maps from the shipped bundle (keep them as a separate CI artifact), minify, and check the size budget (section 7).
- Targets: Windows 10/11 x64 only for v1.0. macOS and Linux later, if at all.

---

## 5. Installer and patcher

### 5.1 Choice of technology
| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **Go CLI + Wails GUI (WebView2)** | Small single-file exe (about 10–15 MB, an estimate to check in M4). No runtime. The GUI is HTML/CSS, so it can use the Nessacord palette and fonts | Need Go and Wails on the build machine | **Recommended** |
| Node SEA (single-file Node app) | Reuses TypeScript and the vencord-lean tooling | Binary about 80–100 MB, slower start | Rejected for size |
| Rust + Tauri | Small exe, web UI | Heavier toolchain for a small app | Acceptable alternative |
| C# .NET (NativeAOT) | Good Windows integration | Larger toolchain, less consistent with the repo | Acceptable alternative |

### 5.2 Modes
- **GUI** (double-click `Nessacord-Setup-x.y.z.exe`): choose channels (Stable and Canary, detected), see status, then Install, Repair, Uninstall or Migrate from Vencord. Log view. Nothing hidden.
- **Portable** (`Nessacord-Patcher-x.y.z.exe`): same code, no install step.
- **CLI** for automation: `--status`, `--install [--channel stable,canary]`, `--repair`, `--uninstall`, `--dry-run`, `--json`, `--quiet`.

### 5.3 Install algorithm
1. Detect channels in `%LOCALAPPDATA%`: `Discord`, `DiscordCanary`, `DiscordPTB`, `DiscordDevelopment`.
2. For each channel:
   a. If its `Discord*.exe` is running, stop and tell the user to close it (the asar is locked while running). Offer to continue after they close it, not to kill it.
   b. List `app-*` folders. **Ignore** any folder beginning with `.` (e.g. `.app-1.0.1316.installing`). Sort by version, not string. Pick the newest complete folder that has `resources\app.asar`.
   c. Classify the current `app.asar`: stock (has a real Discord `package.json` and bundle), Vencord injector (points at `…\Vencord\dist\patcher.js`), Nessacord injector (marker in `index.js`), or unknown (**stop and report**, change nothing).
   d. Back up: rename `app.asar` → `_app.asar` only if `_app.asar` does not already exist. Never overwrite an existing `_app.asar`.
   e. Write the new injector to a temporary name, then atomically rename into place.
3. Copy the Nessacord build into `%APPDATA%\Nessacord\dist\` through a staging folder, then rename it. Verify SHA256 of every file against the release's manifest.
4. Write `%APPDATA%\Nessacord\install.json`: channels, folders patched, versions, hashes, the time of each change.
5. Optional, opt-in: Start Menu shortcut (`Nessacord`), repair task (section 6.5).

### 5.4 Uninstall algorithm
- For each patched channel: check that `app.asar` is the Nessacord injector, then rename `_app.asar` → `app.asar`. If the state is anything else, stop and report.
- Remove the injector only as part of that rename. Never leave Discord without a loadable `app.asar`.
- Remove the scheduled task and Start Menu shortcut if they were added.
- Keep `%APPDATA%\Nessacord` (the user's settings and themes) unless the user ticks "remove my Nessacord data".
- Never touch `%APPDATA%\discord` (Discord's own data, logins and cache).

### 5.5 Migration from Vencord and GodCord
- Detect a Vencord or GodCord install (an injector pointing at `…\Vencord\dist\patcher.js`).
- Offer **Migrate**: copy `%APPDATA%\Vencord\settings\` and `themes\` into `%APPDATA%\Nessacord\` (copies, not moves), then switch the injector. Keep the Vencord data folder so a rollback is possible.
- Note in the UI that the Nessacord and Vencord injectors cannot both be active on one Discord channel. (Stable and Canary can run different builds.)

### 5.6 Safety rules (non-negotiable)
- Never delete a Discord folder or an `app-*` version. Squirrel owns those.
- Never write outside `%LOCALAPPDATA%\Discord*`, `%APPDATA%\Nessacord`, and (opt-in) the Start Menu and the scheduled task.
- No admin rights. If a step needs elevation, say so and skip it.
- Every change is reversible from `install.json`.
- `--dry-run` prints every change it would make and makes none.

---

## 6. Surviving Discord's automatic updates

### 6.1 What happens on an update (from the code and the local folders)
- Discord's Squirrel updater writes a new `app-<version>` folder, with the stock `app.asar` in it. The old folder is usually kept (Canary currently has two), but this is Squirrel's behaviour, not a guarantee.
- While writing, it creates a dot-folder (`.app-1.0.1316.installing`). Anything that only looks for `app-*` will see it, so it must be ignored.
- The new folder is **unpatched**. Vencord's `persistAfterDiscordUpdates` repatches it on `host-updated` or `before-quit`, by copying the injector from the old folder. **Two failure modes in that design:**
  1. If the old folder was pruned, there is nothing to copy from, and the repatch silently does nothing.
  2. If the new version is started by Discord's updater without the old process going through `host-updated` or `before-quit`, nothing repatches it. The user then gets an unpatched launch until a patched version next quits. (Whether this happens in practice depends on Squirrel's sequence, which I have not traced. Treat it as a risk to test, not a confirmed bug.)

### 6.2 Nessacord's fix: generate, don't copy
- The injector is two files and a fixed path. Nessacord writes it from its own code (`index.js` = `require` of `%APPDATA%\Nessacord\dist\patcher.js`, `package.json`). It does not depend on any other folder.
- `persistAfterDiscordUpdates` becomes: find the newest complete `app-*`, check its `app.asar`, and write the generated injector if it is stock. Same trigger points, but it no longer needs the old folder.

### 6.3 Fail-open loading (so a bad patch never locks the user out)
- The injector catches errors from loading `patcher.js`. If loading fails, it loads the original `_app.asar` (vanilla Discord) and records the failure.
- **Crash loop guard:** if Nessacord fails to start in two consecutive launches, the next launch boots vanilla Discord and shows a one-time notice with a "Repair / Reinstall" link. The counter lives in `%APPDATA%\Nessacord\state.json`.
- Vencord's injector (the 72-byte `index.js` in section 2.2) has no such guard. Nessacord adds it.

### 6.4 Patch health (detecting Discord-side breakage early)
- Every patch's `find` and `match` is checked when built: it must match **exactly one module** (vencord-lean's rule), and the plugin health panel reports which ones fail at runtime.
- **Snapshots:** the maintainer captures Discord's webpack module text from Stable and Canary (the `discord-session` skill, `wpsearch`/`wreq.m`) into a snapshot file. Snapshots are **not** kept in the repo (they are large and are Discord's code). They are attached to a release or kept locally. `patch-health.yml` runs the finds against the latest snapshot and fails the build if any find is gone or ambiguous.
- CI does not download Discord itself. Keep it that way.
- In-app: a **Health** panel lists failing patches, missing selectors, and the detected Discord build.

### 6.5 Repair layers (defence in depth)
| Layer | What | Covers |
|---|---|---|
| 1. In-app repatch | `persistAfterDiscordUpdates`, rewritten (6.2) | Updates that happen while Nessacord is running |
| 2. Launcher | Start Menu "Nessacord" shortcut runs `Nessacord-Patcher --repair --quiet`, then starts Discord | Launches through Nessacord's shortcut |
| 3. Logon / periodic repair | Per-user scheduled task (or HKCU `Run` entry) runs `--repair --quiet` at logon and on a timer. Opt-in, disclosed, removed on uninstall | Launches through Discord's own shortcut or a Squirrel update while the PC was off |
| 4. Manual | GUI "Repair" button and `--repair` | Everything else |

- **Unknown to verify (M4):** whether `schtasks` can create a per-user task without elevation on Windows 11, and whether some endpoint protection flags it. If it needs elevation, fall back to the HKCU `Run` entry.

### 6.6 Test plan for updates (section 9)
- Simulate an update: copy a stock `resources` folder into a new `app-<higher>` folder, run `--repair`, confirm the injector is written, and launch.
- Simulate a pruned old folder (delete the previous `app-*`), confirm repair still works.
- Simulate a `.installing` folder, confirm it is ignored.
- Simulate a Discord build that changes the asar layout, confirm the installer stops and reports, and Discord still starts.

---

## 7. Optimisation

### 7.1 Budgets (hard, checked in CI for each release)
| Metric | Budget | How measured |
|---|---|---|
| Nessacord renderer bundle (release build, gzip) | Set in M6 from a measured baseline. The current `dist/renderer.js` (about 815 KB) is a dev build, so it is not comparable to a minified release | Build size check |
| Theme CSS (minified, gzip) | ≤ 40 KB | Build size check |
| Fonts (all weights, woff2, Latin subset) | ≤ 250 KB | Build size check |
| Images (lace, icon, optional defaults) | ≤ 300 KB | Build size check |
| Extra startup from Nessacord plugins | ≤ 50 ms median over 10 cold starts, on the reference machine | Startup timing |
| Extra renderer heap from Nessacord (idle, 10 min) | ≤ 15 MB | DevTools memory snapshot |
| Runtime network calls from Nessacord code and theme | **0** | Network log in a test run |
| Continuous animation | Only the splash/loading pulse; off under reduce-motion | Review |

Budgets are **proposed**. Set the real baselines in M6 and tighten from there. Vencord's README says it is "fairly lightweight", so the target is to add as little as possible on top of vencord-lean.

### 7.2 Baselines to measure first (M6)
- Stock Discord Canary, vencord-lean with GodCord and ToughLove (current state), and Nessacord. Three cold starts each to the logged-in home screen.
- Record: time to interactive, renderer heap, JS parsed, patch-scan time, and CSS size.
- A branch named `feat/startup-timings-tti` appears in a Vencord mirror in my web search. I have not checked what it contains. Check whether it can be reused rather than rebuilt.

### 7.3 Specific optimisations
- **Patch scanning:** verify `patchMatcher.ts` (Wu-Manber multi-pattern search) against the plain `includes()` loop on a captured module snapshot, then measure. Merge it only if it is faster and equal. Do not quote the 5× figure until measured.
- **Lazy theme parts:** the photo and lace layers load after first paint. Nothing in the critical path waits for an image.
- **Class lookups:** cache discovered class names in settings and re-check when idle (ToughLove's approach). Avoid scanning every module on every start.
- **No observers on the message list.** CSS handles message styling. A `MutationObserver` only on the app root for the few DOM swaps (brand, icons), disconnected when unused.
- **CSS cost:** no large `backdrop-filter`, no full-screen blur, no animated `filter`. Use `will-change` only where measured.
- **Images:** WebP for everything; AVIF only if a measurable gain. Downscale the wallpaper to the largest size a user will display, and offer a "small" option.
- **Build:** tree-shaking and minification (already in vencord-lean). Drop source maps from the release bundle. Dedupe the icon data.
- **Idle work:** anything not needed for the first screen goes into `requestIdleCallback`.

---

## 8. Security, privacy and legal

### 8.1 Security
- The installer runs with the user's rights only. No services, no drivers, no admin.
- Every downloaded file is verified against SHA256 sums from the same release. Sums are published in `SHA256SUMS.txt`, with GitHub build-provenance attestations.
- No code is fetched and run at runtime that is not in the signed or hashed release.
- No token handling. Nessacord never reads, stores, copies or transmits Discord tokens. Leave Discord's own storage alone.
- Third-party userplugins and online theme links are **off** by default and clearly labelled.

### 8.2 Privacy
- No telemetry, no analytics, no crash reporting to a third party. The local health log stays local.
- Vencord's `NoTrack` (blocking Discord analytics) stays on by default.
- No external image, font or script hosts in shipped themes (the existing `nessatheme.css` and `toughlove.css` load from `i.ibb.co` and `przemec.github.io`; these must not carry over).
- Cloud settings sync is removed from the UI and off in code (section 4.3c).

### 8.3 Discord's terms and account risk
- Client modifications are against Discord's Terms of Service. Vencord's README says that, and says Discord has been indifferent so far, and that some people choose not to use mods on accounts they cannot afford to lose. Sources I found disagree on how strictly this is enforced. **Nessacord will carry the same risk, and the README must say so plainly.**
- Recommendations to limit exposure:
  - Test on a secondary account only.
  - No automation, selfbot, auto-reply or message-sending features, and no abusive plugins.
  - No age-gate bypass in the public build (see 8.5).
  - Do not redistribute Discord's files. Nessacord patches the user's own installation.
  - Keep the disclaimer from Vencord's README in Nessacord's README, adapted.

### 8.4 Licensing and credits
- Vencord is **GPL-3.0-or-later**. Nessacord is a modified version, so it must be GPL-3.0-or-later too. Keep `LICENSE`, every licence header, and Vencord's copyright notices. Add a `NOTICE` that says what Nessacord changed.
- Credits (in README, CREDITS.md and the About page in the app):
  - **deargod**: author of GodCord and ToughLoveTheme in this repo; the Nessacord rebrand and the original theme work builds on that.
  - **bella**: wording to be confirmed by you (section 13).
  - Vencord and its contributors (Vendicated and others), with a link.
  - Font (Cormorant Garamond, SIL OFL), icon set licence, and any other third-party asset.
- Discord trademarks: the product name avoids "Discord". Keep a "not affiliated with Discord Inc." line.

### 8.5 Content and likeness
- The render's photo shows a real person. A public repo that ships her photo, with a product name derived from her name, can cause problems. My recommendations:
  - Do not bundle the photo. Ship the lace-only default and a user-supplied background setting.
  - Do not present the product as made by, or endorsed by, the performer or her team. The README should say so.
  - Confirm rights to the lace texture and to any image you want to ship (M2).
- **NSFW gate bypass:** your Vencord settings list a plugin named `NSFWGateBypass`. It is not in this repo's source, and by its name it bypasses Discord's NSFW gate, which is an age-verification control. `src/userplugins/nsfwBypass.ts` (its plugin name is `NoNSFWBlur`) only removes the blur overlay. **I recommend excluding anything that bypasses Discord's age gate from the public build.** The blur remover is fine to keep as an optional plugin. Your call (section 13).

---

## 9. Testing and QA

### 9.1 Automated (every PR, in CI)
- Upstream checks, unchanged: `pnpm test:ts`, `pnpm lint`, `pnpm lint-styles`, `pnpm build --dev`, `pnpm build --standalone`, `pnpm generatePluginJson`.
- Theme checks: CSS lint, no `@import` or external `url(` in shipped themes, size budgets (section 7.1), contrast check on the palette (section 3.3).
- Installer unit tests on **fake Discord trees** (temporary folders). Fixtures:
  - Stock, Vencord-patched, Nessacord-patched and unknown `app.asar`.
  - Several `app-*` versions, including a `.installing` folder and a folder with a non-numeric name.
  - Missing `resources`, an empty `app.asar`, a read-only file, and a locked file (simulated with an open handle).
  - User names with spaces and non-ASCII characters in the path (e.g. `C:\Users\Jöhn Doe\`). This is a common installer failure.
- Patch health (section 6.4) against the latest snapshot.

### 9.2 Integration
- Injector test: a stub `_app.asar` package whose `main` writes a marker file. Confirm the injector loads `patcher.js` and then the stub, and that fail-open falls back to the stub.
- Crash-loop test: make `patcher.js` throw twice, confirm the third launch is vanilla and the notice appears.
- Update simulation (6.6), run on a copy of a real install, never the live one.

### 9.3 Live (manual, on a secondary account)
- Canary first, then Stable.
- Checklist per release: login, DM list, a server, channel with messages, image embed, member list, settings (Discord and Nessacord), popout, modal, voice connect/disconnect (UI only), tray, window title, taskbar icon, a Discord update simulation.
- Use the `discord-session` skill's live tools only on a test account.

### 9.4 Visual regression
- A **static demo page** in `docs/demo/`: fake data (no real names, no real messages) laid out with the same classes as the theme's selectors, so the screenshot tests run without a Discord login. Compare against `maininspiration.png` at 1672×941 and report a difference score per region.
- Also screenshot real Discord on the test account at 1920×1080 and 1366×768 for the final check.

### 9.5 Accessibility and responsiveness
- Keyboard focus visible (a 2 px outline in `#f1f0ee`, not removed).
- Reduced motion respected (OS setting and Discord's own setting).
- Text scaling and Windows display scaling at 125% and 150%.

---

## 10. Release and distribution (GitHub)

### 10.1 Repository
- Name: `nessacord`. Owner: you to choose (section 13). Public, GPL-3.0-or-later.
- Branch model: `main` (release-ready), feature branches, tags `vX.Y.Z`. Default branch protected.
- **Pushing is a step I will only do when you tell me to.** No push, no repo creation, until then.

### 10.2 Release assets (per tag)
| Asset | Purpose |
|---|---|
| `Nessacord-Setup-X.Y.Z.exe` | Recommended install for users (GUI) |
| `Nessacord-Patcher-X.Y.Z.exe` | Portable CLI/GUI, same code |
| `nessacord-dist-X.Y.Z.zip` | The Nessacord build, for manual use or the updater |
| `SHA256SUMS.txt` | Checksums for every asset |
| Build provenance attestation | GitHub `attest-build-provenance` on each exe |
| `CHANGELOG.md` entry | What changed, which upstream Vencord version it is based on |

### 10.3 Versioning
- Nessacord uses SemVer (`X.Y.Z`). The About page also shows the upstream base (`Vencord 1.15.16`) and the build commit.

### 10.4 Trust and SmartScreen
- Unsigned executables trigger Windows SmartScreen warnings. Patchers are also sometimes flagged by antivirus. Mitigations: reproducible-ish builds in CI, published checksums, attestations, and clear docs. A code-signing certificate removes most of the warning and costs money (section 13).

### 10.5 Documentation
- `README.md`: what it is, install (one click), uninstall, update behaviour, disclaimer, credits, troubleshooting (crash-loop notice, repair), and "how to switch back to stock Discord".
- `docs/troubleshooting.md`, `docs/patch-health.md` (runbook when Discord changes), `docs/architecture.md`.

---

## 11. Milestones

Sizes are relative effort (S ≈ a day or two, M ≈ about a week, L ≈ two weeks or more, for one developer). Dates depend on your time, so I have not given any.

| # | Milestone | Main deliverables | Acceptance | Size |
|---|---|---|---|---|
| **M0** | Baseline and hygiene | Verify vencord-lean's uncommitted work (`patchMatcher`, `patchWebpack`, the 17 other files) with `pnpm test:ts`, `pnpm build --dev`, `pnpm lint`, plus a before/after scan on a captured module set. Split into commits. Move GodCord, ToughLove and nsfwBypass into tracked source. Set up `C:\nessacord` as the repo. | Clean history, green checks, GodCord still works on this machine | S |
| **M1** | Rebrand core | Product name and data dir, `VENCORD_REMOTE`, window title, taskbar, tray, splash, About page, credits, text rewrite with exclusions, icon pipeline | No "Discord" in the main UI chrome; updater points at Nessacord; no change to Discord's legal text | M |
| **M2** | Visual system | CSS variables (3.3), fonts embedded (3.4), lace tile, photo slot, component rules (3.7), icons, greyscale avatars, settings restyle, demo page | Checklist 3.8 passes; overlay score agreed | L |
| **M3** | Patch resilience | Fail-open injector, crash-loop guard, patch health panel, data-attribute selectors where Discord provides them | A deliberately broken patch does not stop Discord from starting | M |
| **M4** | Installer and patcher | Go CLI, Wails GUI, detection, install, uninstall, status, repair, dry-run, safety rules (5.6), fake-tree tests | All fixtures in 9.1 pass; manual install and uninstall on a test copy | L |
| **M5** | Update resilience and migration | Generated injector, repair layers 1–4 (6.5), `.installing` handling, Vencord and GodCord migration, scheduled-task feasibility check | Update simulations (6.6) pass; migration keeps Vencord data intact | M |
| **M6** | Optimisation | Baselines (7.2), budgets (7.1), `patchMatcher` verified and measured, asset compression, CI size checks | All budgets met on the reference machine | M |
| **M7** | Release | Release workflow, attestations, SHA256 sums, README, NOTICE, CREDITS, disclaimer, repo created and pushed (on your go-ahead) | v1.0.0 installs and uninstalls cleanly on Canary and Stable (test account) | M |
| **M8** | Maintenance | Upstream sync procedure, Discord-change runbook, snapshot refresh process | First upstream sync done with a changelog | Ongoing |

Suggested order: M0 → M1 → M2 (in parallel with M3) → M4 → M5 → M6 → M7. The installer (M4) can start once M1 fixes the data dir and injector path.

---

## 12. Risks register

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R1 | Discord changes class names, modules or intl hashes; patches and icons break | High | Medium | Health panel, patch-health CI, data-attribute selectors where available, fail-open, prefer CSS variables |
| R2 | Discord updater replaces the folder; the patch is lost | High | High | Repair layers 1–4 (6.5), generated injector (6.2) |
| R3 | Auto-updater pulls upstream Vencord and overwrites Nessacord | Medium | High | `VENCORD_REMOTE` fixed at build; a test that asserts the remote; SHA256 verification |
| R4 | GodCord and ToughLove source is lost (gitignored) | Medium | High | M0: move into tracked source now |
| R5 | `patchWebpack` / `patchMatcher` changes regress patching (claims not measured) | Medium | High | M0 verification; separate commits; equal-output test; measure before merging |
| R6 | Discord ToS action against accounts | Low–Medium (sources differ) | High (for the affected user) | Disclaimer, alt-account testing, no abusive features, no age-gate bypass |
| R7 | Photo or name rights issue in a public repo | Medium | Medium–High | Do not bundle the photo; user-supplied background; no endorsement implied; decide on the name (13) |
| R8 | Vencord upstream diverges; merges become painful | Medium | Medium | Keep internal identifiers; small named commits; tag-based sync |
| R9 | SmartScreen or antivirus flags the installer | Medium | Medium | Checksums, attestations, optional signing, clear docs |
| R10 | Theme makes Discord slow (blur, large images, observers) | Medium | Medium | Budgets (7.1), measured baselines, no observers, no heavy blur |
| R11 | Cormorant hurts readability at small sizes, or muted text fails contrast | Medium | Medium | Size tuning, contrast values (3.3), user font-size control |
| R12 | Photo looks soft (750 px source, upscaled) | High | Low | Ask for 2000 px+ source; user-supplied photo anyway |
| R13 | Coexistence problems: two injectors on one channel, data-dir collisions | Medium | Medium | Detection, explicit replace/migrate flow, separate data dir |
| R14 | Windows-only v1.0 excludes other users | Certain | Low | Stated scope; plan macOS/Linux later |
| R15 | Discord running during patch → locked files, partial writes | Medium | Low–Medium | Process check, atomic renames, staging folders |
| R16 | Name conflicts (trademark or existing project) | Unknown | Medium | Proper name clearance before public release |

---

## 13. Open questions and decisions

Each one changes what I build. My recommended answer is first.

1. **Repo owner.** Which GitHub account or org should own `nessacord`? (`gh` is not installed here. I will need you to create or log in to the account, or give me the owner name.)
2. **bella's credit.** What did bella do, and how should it read? (Example: "bella: lace and artwork" or "bella: design".) I have only the name.
3. **Photo.** Bundle the photo in the public release, or ship lace-only with a user-supplied background? **Recommended: user-supplied.** If you want it bundled, confirm you hold the rights and whether a 2000 px source exists.
4. **Lace texture rights.** Do you hold the rights to ship `inspiration1.jpg` as a tile? **Recommended: yes, confirm; otherwise ship without it.**
5. **Server bar.** Keep the server list (styled), or hide it as the render does? **Recommended: keep it**, since the render's missing bar is a mock-up gap.
6. **Nitro and Shop labels.** Keep (functional names) or rename? **Recommended: keep.**
7. **Age-gate bypass.** Exclude anything that bypasses Discord's age gate from the public build? **Recommended: exclude.** The blur remover stays as an optional plugin.
8. **Channels for v1.0.** Stable and Canary (both installed here), or add PTB and Development? **Recommended: Stable and Canary.**
9. **This machine.** Switch Discord Stable and Canary from Vencord to Nessacord at the first test, or use a separate test copy first? **Recommended: test copy first.** (Both channels are currently Vencord-patched.)
10. **Installer.** One setup EXE, or setup EXE plus a portable EXE? **Recommended: both, from the same code.**
11. **Code signing.** Buy a certificate, or accept SmartScreen warnings for v1.0? **Recommended: accept for v1.0, revisit later.**
12. **Platform.** Windows 10/11 x64 only for v1.0? **Recommended: yes.**
13. **"Support Vencord" and "optimised".** Confirm the interpretation in section 1.

---

## 14. What happens next (once you approve)

Nothing in this document has been executed. The next actions, in order, after you answer section 13:

1. **M0 (verification, no push):** run `pnpm test:ts`, `pnpm build --dev` and `pnpm lint` on vencord-lean's current uncommitted state; measure `patchMatcher` against `includes()` on captured modules; report results before committing anything.
2. **Set up the repo locally** at `C:\nessacord` with history from vencord-lean. Leave `C:\Users\spicy\dev\vencord-lean` untouched.
3. **Move GodCord, ToughLove and nsfwBypass** into tracked source inside the new repo (with the licence headers they already carry).
4. Start **M1** (rebrand core) and the **asset pipeline** (`tools/make-assets.py` from the `nessacordicon.png` source, plus the lace tile).
5. Pause before creating the GitHub repo or pushing anything, and wait for your go-ahead (owner, visibility, and the first push).

I will not touch the live Discord installs on this machine until you say so.
