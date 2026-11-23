# IronFrame OuterFortress (Cart) 01 — User Guide (`v0.1.0`)

User guide for **`IronFrame OuterFortress (Cart) 01`** (`IronFrame-OuterFortress-Cart-01 v0.1.0`) — a clean-room Windows static recompilation cartridge module running on **`reCart v0.1.3`** and **`reEngine v0.6.2`**.

## 1. What It Is & Who It's For

`IronFrame OuterFortress (Cart) 01` provides clean-room hardware/bank metadata, static recompilation seed tables (`499` seeds + `$C6B7` jump-table dispatch across all `8` UXROM banks), and a lightweight `< 2 MB` Windows launcher for your legally obtained personal `NES-ME-USA` cartridge dump. Instead of emulating instructions one-by-one in an unoptimized interpreter loop, `reCart` ahead-of-time compiles your local cartridge dump into a cached native `.NET 8.0` module (`<dataDir>\cache\`) using the clean-room recipe inside `app\IronFrame-OuterFortress-Cart-01.recomp`.

> **Strictly Clean-Room Distribution:** This package ships zero copyrighted cartridge ROMs, sprites, audio samples, or pre-generated retail machine code. You supply your own verified dump under `data\rom\` (`.nes`, `.prg`, `.zip`, or `.7z`), or choose one interactively via the first-run **Clean-Room Media Gate**.

## 2. Install, Upgrade & Uninstall

Choose one of two official Windows delivery packages from `StreamItAllGit/IronFrame-OuterFortress-Cart-01-Recomp` (default install directory: `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\IronFrame-OuterFortress-Cart-01\`):

- **Online Setup (`IronFrame-OuterFortress-Cart-01-Setup-v0.1.0-win-x64.exe`):** Compact bootstrap installer that auto-detects and SHA-256 verifies a sibling `Portable.zip` in the same folder (or fetches it over HTTPS from GitHub Releases / accepts `--offline-bundle <path>`).
- **Offline Setup (`IronFrame-OuterFortress-Cart-01-Offline-Setup-v0.1.0-win-x64.exe`):** Self-contained setup with the verified payload and optional `Full` profile `reEngine v0.6.2` + `reCart v0.1.3` runtime installers bundled inside; works with zero internet access.
- **Portable Archive (`IronFrame-OuterFortress-Cart-01-v0.1.0-win-x64-Portable.zip`):** Extract anywhere and run `IronFrame-OuterFortress-Cart-01.exe` with `--data-dir .\data`.
- **Two Install Profiles:** `Lightweight` (cartridge module + launcher only) and `Full` (automatically detects or installs companion `reEngine v0.6.2` + `reCart v0.1.3` cleanly without nested `reEngine` folders).
- **Safe Upgrades & Zero Auto-Start:** `AllowAutoStart = false` (`0` background startup services). Re-running setup preserves your personal `data\rom\` cartridge image, `data\config\settings*.json` settings, and save files automatically.

## 3. First-Run Setup Walkthrough

1. Dump your legally owned **NES-ME-USA** cartridge into a standard `131,088`-byte `.nes` file (SHA-256 `79095355436222161cb3841ff9b5c7a572e77af11461935e433bd6e8dfd0f164`) or headerless `131,072`-byte `.prg` image (SHA-256 `f6d29afbd7ddad33672852232791a396695de2e77dccb83088a8d6b139d8c9cb`) — optionally compressed as `.zip` or `.7z`.
2. Place the verified dump in `<InstallDir>\data\rom\` (or pass `--preload-media <path>` / `--rom <path>`). The launcher also checks compatible SHA-256-matched sibling `reEngine\games\*\data\rom\` paths automatically.
3. Launch **`IronFrame-OuterFortress-Cart-01.exe`** from your Start Menu, Desktop shortcut, or install folder.
4. If `data\rom\` has no verified dump yet — or if you open an unmatched raw ROM directly without a `.recomp` module — the **Clean-Room Media Gate** opens cleanly instead of launching a placeholder screen or crashing: click **`Select Media...`**, **`Select .recomp Module...`**, **Drag & Drop** your verified `.nes`/`.prg`/`.zip`/`.7z`/`.recomp` file onto the window (corrupt files retain a clear warning banner), or click **`Launch Demo / Test Cart`** to verify the clean-room runtime without retail media. External verified ROMs chosen interactively are copied safely into `<InstallDir>\data\rom\`.
5. Once verified, the window opens immediately with live `"Preparing Native Module..."` progress while `reCart` compiles your local clean-room AOT module inside `<dataDir>\cache\` and boots into the `60.0988 Hz` NTSC game window. Any Video/Audio adjustments you make in the `F1` drawer automatically persist across restarts.

## 4. Screen-by-Screen & Controls Tour

- **Game Viewport (`OpenGL` default; `Vulkan` / `DirectX` / `ParallelCpu` / `Software` selectable):** Pixel-sharp integer scaling (`2x` default) with `nativePar`, `fourByThree`, `squareInteger`, `nativeFit`, and `horPlus16By9` projection options.
- **Default Keyboard Bindings (`settings.controls.json`):**
  - **D-Pad:** `Arrow Keys` or `WASD`
  - **A (Action / Punch / Confirm):** `X` or `K`
  - **B (Equipment / Cancel):** `Z` or `J`
  - **Start (Inventory):** `Enter`
  - **Select (Transceiver Menu):** `Right Shift` or `Tab`
  - **Overlay / System Keys:** `F1` (`Settings` drawer), `F3` (Performance HUD), `F5` / `F9` (Quick Save / Quick Load when supported by host), `F11` (Borderless Fullscreen).

## 5. Verification & Troubleshooting

- **"No BYO cartridge dump found" / Checksum Mismatch:** Confirm your file under `<InstallDir>\data\rom\` is not empty and matches the `131,088`-byte `.nes` or `131,072`-byte `.prg` SHA-256 listed above.
- **"No compatible reCart runtime was found":** Re-run setup selecting the **`Full`** profile, install `reEngine v0.6.2` + `reCart v0.1.3`, or launch with `--reengine-dir <dir>` / `--suite-dir <dir>`.
- **Preflight Self-Check:** Run `IronFrame-OuterFortress-Cart-01.exe --verify --data-dir "<InstallDir>\data"` in PowerShell to inspect resolved runtime paths and cartridge validation status cleanly.

## 6. License & Legal

Clean-room code and recipe metadata are licensed under the [`MIT License`](../../LICENSE) (`Copyright (c) 2026 StreamItAll`). **IronFrame OuterFortress (Cart) 01** is an independent clean-room fan preservation and static-recompilation research project (`IronFrame-OuterFortress-Cart-01-Recomp`) by StreamItAll; it is not affiliated with, endorsed by, or sponsored by Konami Digital Entertainment Co., Ltd. or Nintendo Co., Ltd. *Metal Gear* and *Nintendo Entertainment System* are trademarks of their respective owners, referenced here solely to identify compatible original hardware media supplied by the end user. Ships zero retail ROM bytes or copyrighted assets.
