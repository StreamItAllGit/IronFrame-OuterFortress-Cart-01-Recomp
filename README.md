<div align="center">

<img src="Dev/assets/IronFrame-OuterFortress-Cart-01.png" alt="IronFrame OuterFortress (Cart) 01 Recomp Logo" width="96" />

# IronFrame OuterFortress (Cart) 01

Clean-room `.NET 8.0` static recompilation cartridge module built on `reCart` and `reEngine`, preserving authentic `60.0988 Hz` gameplay with strictly opt-in modern enhancements.

![Release](https://img.shields.io/github/v/release/StreamItAllGit/IronFrame-OuterFortress-Cart-01-Recomp?include_prereleases&style=flat) [![CI](https://github.com/StreamItAllGit/IronFrame-OuterFortress-Cart-01-Recomp/actions/workflows/ci.yml/badge.svg)](https://github.com/StreamItAllGit/IronFrame-OuterFortress-Cart-01-Recomp/actions/workflows/ci.yml) [![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-blue?style=flat)](https://streamitallgit.github.io/IronFrame-OuterFortress-Cart-01-Recomp/) ![License](https://img.shields.io/badge/license-MIT-green?style=flat) ![Runtime](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat) ![Platform](https://img.shields.io/badge/platform-Windows-lightgrey?style=flat)

**Active Horizon (`v0.1.0` Shipped -> `v0.2.0` Next):** `██████████` **100% (`v0.1.0` Baseline Complete)** — targeting `v0.2.0` Multi-Scene Lockstep & Audio DSP

</div>

---

## About

> **Beta (Pre-1.0):** `IronFrame OuterFortress (Cart) 01` (`ironframe-outerfortress-cart-01`, `v0.1.0`) is an active pre-1.0 clean-room static recompilation project targeting `v1.0.0` Windows desktop stability and `v2.0.0` cross-platform portability.

**IronFrame OuterFortress (Cart) 01** is a slim, zero-asset static recompilation cartridge (`ironframe-outerfortress-cart-01`) for the `reCart` platform engine (`v0.1.3`) and `reEngine` suite host (`v0.6.2`). Generated from read-only hardware and decompilation schemas in the upstream `IronFrame-OuterFortress-Cart-01-Decomp` research archive, it lifts `499` game functions locally on the player's machine into `<dataDir>\cache\` from a legally owned cartridge dump placed inside `data\rom\`, shipping zero copyrighted ROM bytes, graphics, audio, or pre-compiled game assemblies.

> **Public Dev-Log & Showcase:** Derived from a clean-room `6502` decompilation and execution-lockstep study (`499` indexed functions across `8` `UNROM` banks, `37` entry anchors, `$C6B7` inline jump-table lifting across fixed/switchable banks, `17` bank trampolines, and `3` WRAM SoA watches), this module compiles and verifies `20,002`-step differential AOT execution (`mismatchedSteps=0`) against `reCart v0.1.3`'s reference oracle at `60.0988 Hz` default timing.

## Key Features

- **Authentic `60.0988 Hz` Default with Opt-In Enhancements:** Preserves original physics, guard AI, alert timing, and collision semantics by default while unlocking opt-in modern display scaling, aspect presets, automatically persisted `F1` Video/Audio settings (`settings.json`, `settings.video.json`, `settings.audio.json`), jitter-free `F3` telemetry, reliable keyboard menu input, and future `v0.2.0+` audio/SubTick/vault milestones.
- **2-Member Clean-Room Cartridge (`.recomp`):** Packages strictly two UTF-8 JSON metadata entries (`module.recomp.json`, `aot-recipe.json`) compiled asynchronously on first launch behind an instant window and live `"Preparing Native Module..."` overlay into `<dataDir>\cache\`—zero retail ROM bytes or generated DLLs are distributed.
- **Multi-Format BYO Verification & Clean-Room Media Gate (`NES-ME-USA`):** Automatically detects and SHA-256 verifies `.nes` (`131,088` B, `SHA-256 = 79095355436222161cb3841ff9b5c7a572e77af11461935e433bd6e8dfd0f164`), headerless `.prg` (`131,072` B, `SHA-256 = f6d29afbd7ddad33672852232791a396695de2e77dccb83088a8d6b139d8c9cb`), `.zip`, and `.7z` dumps inside `data\rom\` (or sibling suite paths), and opens an interactive **Clean-Room Media Gate** (`Select Media...`, `Select .recomp Module...`, Drag & Drop, or `Launch Demo / Test Cart`) with persisted warning banners whenever `data\rom\` is empty or an archive fails validation.
- **Clean-Root `< 2 MB` Launcher & `DualSetup8` Installer:** Ships a single-file launcher plus Online Setup, Offline Setup (with bundled `reEngine` + `reCart` and `--offline-bundle`), and Portable ZIP (`AllowAutoStart = false`), preserving `data\rom\`, settings, and saves across upgrades.

## Getting Started

### Prerequisites

- **Windows 10 / 11 (`win-x64`)**
- **.NET 8.0 Desktop Runtime** (included self-contained inside `DualSetup8` installers; SDK required for building from source)
- **`reEngine` (`>= 0.6.2`) and `reCart` (`>= 0.1.3`)** *(automatically installed when choosing the `Full` profile in Offline Setup, or supplied via `--suite-dir` / `--reengine-dir`)*
- A legally owned cartridge dump (`NES-ME-USA.nes`, `.prg`, `.zip`, or `.7z`) placed inside `data\rom\` (or chosen interactively at first launch via the Clean-Room Media Gate)

### Releases & Downloads

Official signed packages are published on **GitHub Releases**:
- **Online Setup:** `IronFrame-OuterFortress-Cart-01-Setup-v0.1.0-win-x64.exe` (downloads and SHA-256 verifies the Portable bundle, or auto-detects an adjacent / `--offline-bundle` verified archive)
- **Offline Setup:** `IronFrame-OuterFortress-Cart-01-Offline-Setup-v0.1.0-win-x64.exe` (self-contained offline bundle with `reEngine` + `reCart` runtime installers)
- **Portable Archive:** `IronFrame-OuterFortress-Cart-01-v0.1.0-win-x64-Portable.zip`

### Quick Try

```powershell
# Place your verified cartridge dump in data\rom\ and launch the module
.\IronFrame-OuterFortress-Cart-01.exe

# Or open the clean-room .recomp cartridge directly through reEngine
reEngine.exe --open ".\app\IronFrame-OuterFortress-Cart-01.recomp"
```

<details>
<summary>Developer build, CLI overrides, and isolated test flags</summary>

```powershell
# Clone and run isolated zero-asset unit tests (50 tests, /warnaserror)
git clone https://github.com/StreamItAllGit/IronFrame-OuterFortress-Cart-01-Recomp.git
cd IronFrame-OuterFortress-Cart-01-Recomp
$env:APP_ISOLATED_TEST = "1"
dotnet test Dev\IronFrameCart01.slnx -c Release /warnaserror

# Launch with explicit isolated runtime storage, config, and log directories
.\IronFrame-OuterFortress-Cart-01.exe --verify --data-dir ".\tmp-data" --config ".\tmp-data\config\settings.json" --log-dir ".\tmp-logs"
```

| CLI Flag | Purpose |
| :--- | :--- |
| `--open <path>`, `--module <path>` | Override the `.recomp` cartridge path (`app\IronFrame-OuterFortress-Cart-01.recomp` by default) |
| `--rom <path>`, `--preload-media <path>` | Supply an explicit BYO cartridge file instead of scanning `data\rom\` |
| `--reengine-dir <dir>`, `--suite-dir <dir>` | Highest-precedence `reCart` runtime discovery overrides |
| `--data-dir <dir>`, `--config <path>`, `--log-dir <dir>`, `--save-dir <dir>` | Redirect writable runtime storage, settings, logs, and saves (isolated under `APP_ISOLATED_TEST=1`) |
| `--backend <id>` / `--aspect-mode <mode>` | Select video backend (`opengl`, `vulkan`, `directx`, `parallelCpu`, `software`) and aspect preset |
| `--lockstep-oracle`, `--lockstep-steps <n>` | Compare local AOT execution step-by-step against `reCart`'s quarantined reference oracle |
| `--dry-run`, `--verify`, `--wait` | Run fail-closed preflight verification or synchronous wait semantics |

</details>

## Project Structure

- `Dev/` — Source code (`RecipeGen`, `Module`, `Launcher`, `Installer`), unit tests (`IronFrameCart01.Tests`), manifests, clean-room assets, and manuals
- `Builds/` — Local compiled staging builds
- `Releases/` — Signed `DualSetup8` installer bundles, Portable ZIP, `SHA256SUMS.txt`, and verification notes
- `Archive/` — Compressed clean-room source snapshots

## Documentation

- [Project Charter](Dev/docs/CHARTER.md) — Core identity, non-goals, and authentic-vs-enhanced principles
- [Features](FEATURES.md) — Capability registry and implementation status
- [Roadmap](ROADMAP.md) — Release milestones from `v0.1.0` to `v1.0.0` Gold and beyond
- [Changelog](CHANGELOG.md) — Version history ledger
- [Contributing](CONTRIBUTING.md) — Contribution guidelines and clean-room rules
- [User Guide](Dev/docs/USER_GUIDE.md) — End-user setup, controls, and troubleshooting
- [Architecture](Dev/docs/ARCHITECTURE.md) — System design, symbol index, and module boundaries
- [Interfaces](Dev/docs/INTERFACES.md) — Contracts, CLI flags, and manifest schemas
- [Operations](Dev/docs/OPERATIONS.md) — Verification workflows and maintenance

## Code Signing Policy

- Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/), for official release installers and launcher executables published from this repository.
- **Committers and maintainers:** Only project maintainers (`StreamItAll`) possess commit, release tag, and signing-workflow trigger privileges.
- **Reviewers and contributors:** All pull requests and release inputs undergo deterministic build checks, zero-asset unit tests (`APP_ISOLATED_TEST=1`, `/warnaserror`), and maintainer review before release promotion.
- **Privacy & clean-room policy:** Signed binaries contain zero telemetry, zero background startup services (`AllowAutoStart = false`), and zero copyrighted retail data; all user cartridge media stays strictly local in `data\rom\`.

## License & Legal

Distributed under the **MIT License** ([LICENSE](LICENSE), `Copyright (c) 2026 StreamItAll`). See [SECURITY.md](SECURITY.md) for security boundaries and vulnerability reporting.

**BYO ROM & Non-Affiliation Notice:** This clean-room software project is developed independently and contains **zero** copyrighted game ROMs, graphics, audio, or scripts. It is built for **Metal Gear (NES, USA)** (`NES-ME-USA`); players must supply their own legally obtained cartridge dump inside `data\rom\`. *Metal Gear* is a trademark of Konami Digital Entertainment Co., Ltd., and *NES* / *Nintendo Entertainment System* are trademarks of Nintendo Co., Ltd. Neither trademark holder endorses or is affiliated with this project.
