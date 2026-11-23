# IronFrame OuterFortress (Cart) 01 — Interfaces & Contracts (`v0.1.0`)

Documents the clean-room `.recomp` module manifest ABI, validated BYO cartridge digests, CLI contracts for `IronFrame-OuterFortress-Cart-01.exe` and `IronFrame-OuterFortress-Cart-01-Setup`, and split-domain settings schema.

## 1. Fast-Path Lookup & Routing Table

| Component | Entry Point / Contract | Purpose | Source |
| :--- | :--- | :--- | :--- |
| Clean-Room Package | `IronFrame-OuterFortress-Cart-01.recomp` | 2-member ZIP (`module.recomp.json` + `aot-recipe.json`) | [module.recomp.json](../module.recomp.json) |
| Recipe Generator | `IronFrameCart01.RecipeGen --schemas-dir <dir> --out <path>` | Emits deterministic `499`-seed clean-room recipe JSON | [RecipeGen/Program.cs](../src/IronFrameCart01.RecipeGen/Program.cs#L19-L87) |
| Cartridge Module | `Cart01Constants`, `ByoRomValidator`, `Cart01RecipeLoader` | Validates BYO `.nes`/`.prg` digests and packs/validates `.recomp` | [Cart01RecipeLoader.cs](../src/IronFrameCart01.Module/Cart01RecipeLoader.cs) |
| Clean-Root Launcher | `IronFrame-OuterFortress-Cart-01.exe` | Embeds runtime contracts (`1,032,184 B`), runs pre-flight `.nes`/`.prg`/`.zip`/`.7z` discovery or Clean-Room Media Gate routing, seeds settings, launches `reCart` | [Launcher/Program.cs](../src/IronFrameCart01.Launcher/Program.cs#L53-L144) |
| Dual-Setup Installer | `IronFrame-OuterFortress-Cart-01-{Setup,Offline-Setup}-v0.1.0-win-x64.exe` | Online/Offline installs into coded suite paths preserving `data\rom\` & `data\config\` | [Installer/Program.cs](../src/IronFrameCart01.Installer/Program.cs#L30-L265) |

## 2. Clean-Room Cartridge ABI (`reCart v0.1.3`)

- **Module ID & Mapper:** `"ironframe-outerfortress-cart-01"`, `"platformId": "nes"`, `"engineId": "reCart"`, `"engineVersion": "0.1.3"` (`0.1.x` track; hosts `reEngine v0.6.2` `hostProtocolVersion: "0.6.2"`), UNROM Mapper `2` / Submapper `0`.
- **Entry & Dispatch:** `"entryAssembly": "aot-recipe.json"`, `"defaultBootExecutable": "PRG_FIXED_07:0xC001"`, `"defaultFramerateMode": "lockedCap"`, `"executionTierMode": "staticAotOnly"`.
- **Structural Recipe Features:** `499` schema-derived seeds across fixed `PRG_FIXED_07` and switchable `BANK_00..06`, `37` entries (`0xC001` RESET, `0xC06C` NMI, `0xC0B7` IRQ), `jumpTables: ["0xC6B7"]` lifted cleanly across fixed/switchable banks (`0` hardcoded retail ROM addresses), `17` UNROM bank trampolines (`$C932..$C958`, `$FFD0..$FFD7`), and `3` WRAM watch slices (`$0200` OAM `256` B, `$0300` actors SoA `128` B, `$0380` audio SoA `128` B).
- **Host / IPC Boundaries:** `AllowAutoStart = false`; clean job termination through `Foundation.Core.JobObjectGuard`; `--automation-port` disabled (`ExitCode = 2`) in standard Release builds; instant window launch with async `HostAotCompileOverlayView`, `HostSettingsStore` live `F1` Video/Audio persistence, `0 px` jitter `F3` HUD, 2-frame deferred synthetic tap auto-release, and standalone raw-ROM Clean-Room Media Gate guidance.

## 3. Launcher CLI Contract (`IronFrame-OuterFortress-Cart-01.exe`)

Relative path arguments are normalized against caller CWD before launching `reCart`; `--autostart` / `--auto-start` tokens are stripped fail-closed (`LauncherCliAndContainment.SanitizeArgs`).

| Flag | Forwarded / Handled Behavior |
| :--- | :--- |
| `--reengine-dir <dir>` / `--suite-dir <dir>` | Highest-precedence runtime discovery overrides ahead of bundled/registry paths |
| `--open <path>`, `--module <path>` | Override target `.recomp` or manifest (default `<BaseDir>\app\IronFrame-OuterFortress-Cart-01.recomp`) |
| `--rom <path>`, `--preload-media [<path>]` | Explicit BYO `.nes` (`131,088` B), headerless `.prg` (`131,072` B), `.zip`, or `.7z` path; automatically scanned by [`LauncherMediaDiscovery`](../src/IronFrameCart01.Launcher/LauncherMediaDiscovery.cs#L1-L291) (`ResolveSevenZipOverridePath` + `MediaContainerExtractor` with ephemeral temp cleanup) or forwarded as `--preload-media` to open the Clean-Room Media Gate when no verified ROM exists locally |
| `--data-dir <dir>`, `--config <path>`, `--log-dir <dir>`, `--save-dir <dir>` | Contained runtime roots (required inside boundary under `APP_ISOLATED_TEST=1`) |
| `--lockstep-oracle`, `--lockstep-steps <n>` | Runs `n`-step differential AOT-vs-`InterpreterOracle` verification (`0` mismatches gate) |
| `--backend`, `--aspect-mode`, `--scale`, `--region`, `--execution-tier` | Presentation (`opengl\|vulkan\|directx\|parallelCpu\|software`) and execution controls |
| `--verify`, `--dry-run`, `--wait`, `--frames <n>`, `--dump-screenshot <png>`, `--dump-audio <wav>` | Verification, preflight dry-run, deterministic frame/audio capture, synchronous wait |
| `--version`, `--help` | Prints clean-room product banner or usage summary (`ExitCode = 0`) |

## 4. Installer CLI Contract (`IronFrame-OuterFortress-Cart-01-{Setup,Offline-Setup}`)

| Flag | Behavior |
| :--- | :--- |
| `--profile <lightweight\|full>` | `lightweight` installs module + launcher; `full` bootstraps non-nested `reEngine` (`<suite>\`) + `reCart` (`<suite>\engines\reCart\v0.1.3\`) |
| `--install-dir <dir>`, `--suite-dir <dir>`, `--reengine-dir <dir>` | Suite-first target game directory (default `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\IronFrame-OuterFortress-Cart-01`) and suite root |
| `--offline-bundle <zip>` | Supplies verified local `Portable.zip` to Online Setup for zero-network install (auto-detected if verified `Portable.zip` sits beside Setup EXE) |
| `--silent`, `--no-launch`, `--no-shortcuts` | Headless unattended install (`AllowAutoStart = false`, preserves `data\rom\` & `data\config\settings*.json`) |
| `--verify`, `--dry-run`, `--uninstall`, `--purge-data` | Payload SHA-256 verification, dry-run inspection, clean uninstall (with optional purge) |
| `--data-dir <dir>`, `--config <path>`, `--log-dir <dir>` | Isolated verification containment roots (`ExitCode = 3` on escape under `APP_ISOLATED_TEST=1`) |

## 5. Validated BYO Cartridge Digests ([byo_media_sha256.json](../assets/byo_media_sha256.json))

| Variant | Size (Bytes) | CRC32 | SHA-256 (`expectedSha256`) | Required Flag |
| :--- | :--- | :--- | :--- | :--- |
| Headered Container (`NES-ME-USA.nes`, iNES/NES 2.0) | `131,088` | `25BA3D04` | `79095355436222161cb3841ff9b5c7a572e77af11461935e433bd6e8dfd0f164` | `isRequired: true` |
| Headerless PRG Payload (`NES-ME-USA.prg`, `CartPrgHeaderSynthesizer`) | `131,072` | `817431EC` | `f6d29afbd7ddad33672852232791a396695de2e77dccb83088a8d6b139d8c9cb` | `isRequired: false` |

## 6. Settings Schema Reference

Managed across `<dataDir>\config\` via `SettingsDomainStore`: `settings.json` (`appId`, `useGlobalFormatRules`, `engineId`, `compatibleEngineTrack`, `installProfile`, `executionTierMode`), `settings.video.json` (`backendId`, `aspectRatioMode`, `resolutionScale`, `isFullscreen`, `isVsyncEnabled`, `targetFps`, `isFpsOverlayEnabled`), `settings.audio.json` (`audioDriver`, `masterVolume`, `isAudioMuted`), and `settings.controls.json` (`profileMode`, `useGlobalFormatRules`).

## 7. Versioning & Engine Compatibility

- **Project SemVer:** `v0.1.0` (`IRONFRAME_OUTERFORTRESS_CART_01` / `ironframe-outerfortress-cart-01`).
- **Locked SDK Baseline:** `reCart v0.1.3` (`0.1.x` track), `reEngine v0.6.2`, `Foundation v0.5.5`.
- **Public Distribution:** GitHub Releases at `StreamItAllGit/IronFrame-OuterFortress-Cart-01-Recomp`.
