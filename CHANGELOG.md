# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-11-23

### Added
- Authored `IronFrameCart01.RecipeGen` (`OracleSchemaLoader`, `AotRecipeEmitter`) reading upstream clean-room schemas read-only to emit deterministic `499`-seed `aot-recipe.json` metadata (`37` entry vectors/anchors, `$C6B7` RTS jump table, `17` UNROM `$C932..$C958` / `$FFD0..$FFD7` trampolines, and `$0200`/`$0300`/`$0380` WRAM watches).
- Authored `IronFrameCart01.Module` (`Cart01Constants`, `ByoRomValidator`, `Cart01MemoryWatch`, `Cart01RecipeLoader`) and the 2-member clean-room `.recomp` packager producing `IronFrame-OuterFortress-Cart-01.recomp` (`[module.recomp.json, aot-recipe.json]`, zero DLLs, zero pre-lifted C#, zero copyrighted bytes).
- Authored the `< 2 MB` (`1,032,184 B`) single-file `IronFrame-OuterFortress-Cart-01.exe` launcher (`IronFrameCart01.Launcher`) embedding core runtime contracts (`Foundation.{Core,Launcher,Retro}`, `reEngine.Core`, `reCart.Contracts`, `settings.example.json`) with `AllowAutoStart = false`, deterministic `reCart` discovery precedence, `LauncherMediaDiscovery` pre-flight `.nes`/`.prg`/`.zip`/`.7z` discovery (`ResolveSevenZipOverridePath` + `Foundation.Retro.MediaContainerExtractor` + bundled `tools\7z.*` with ephemeral cleanup) and `--preload-media` Clean-Room Media Gate routing, CWD path rebasing, split-domain `SettingsDomainStore` config seeding, and Release `--automation-port` `ExitCode = 2`.
- Authored the `DualSetup8` installer (`IronFrameCart01.Installer`: `IronFrame-OuterFortress-Cart-01-{Setup,Offline-Setup}-v0.1.0-win-x64.exe` and `Portable.zip`) with `InstallerPathResolver` suite-first resolution into coded `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\IronFrame-OuterFortress-Cart-01\`, non-nested `reEngine` + `reCart` bootstrapper (`Lightweight` / `Full`), verified adjacent Portable ZIP auto-discovery, `LastInstallBaseDir` self-healing, and `data\rom\` + `data\config\settings*.json` upgrade preservation (`AllowAutoStart = false`).
- Added `50` zero-asset unit tests (`IronFrameCart01.Tests`, including `PrelaunchRegressionTests`) covering recipe generation determinism, `.nes`/`.prg`/`.zip`/`.7z` BYO discovery & corrupt archive cleanup, `$C6B7` clean-room inline jump-table lifting across fixed/switchable banks, launcher containment/precedence, `LauncherMediaDiscovery` pre-flight discovery & Clean-Room Media Gate routing, suite-first coded path resolution, adjacent Portable discovery, and installer upgrade preservation + loopback HTTP `200`/`404`/tamper checks, plus full 4-drive Sandbox E2E (`[S2-00]`..`[S2-09]` incl. `[S2-06]` Missing-ROM Media Gate `2.68s` + Demo Cart + corrupt `.7z` banner, `[S2-07]` Cold `.7z` AOT boot `4.21s` + `.zip`/`.prg`/`.nes` matrix + `Enter` gameplay progression past title + `0 px` jitter `F3` HUD `w=623px` + QuickSave/Load + WAV `peakPcm=16286`, `[S2-08]` persisted `F1` `Scale=4x`/`VSync=true`/`Master Volume 70%` across quit/relaunch, and `[S2-09]` standalone `reCart.Player.exe` raw-ROM `.recomp` guard `2.18s`) and `20,002`-step differential lockstep verification (`mismatchedSteps=0`).
- Added project charter, clean-room BYO checksum catalog, sonar/tactical icons, security/contributing governance, and updated user/architecture/interface/operations manuals.

### Changed
- Aligned `v0.1.0` runtime and manifest contracts onto additive `reCart v0.1.3` (`LifterJumpTableScanner` clean-room `$C6B7` lifting, async `HostAotCompileOverlayView` instant-window boot, `.7z` support, `MediaGateHandlers` `.recomp` selector & standalone raw-ROM guard), `reEngine v0.6.2` (live overlay controls, `HostSettingsStore` `SettingsDataDirectory` persistence, `0 px` jitter `F3` HUD, 2-frame synthetic non-hotkey tap release, Browse error banners), and `Foundation v0.5.5` with coded clean-room identifiers (`ironframe-outerfortress-cart-01`, `IronFrame-OuterFortress-Cart-01.recomp`) and `"defaultFramerateMode": "lockedCap"`.
- Replaced all hardcoded workstation paths in packaging and verification scripts with relative workspace discovery and environment overrides (`MGR_BYO_ROM_PATH`) for zero-leak public distribution.
