# IronFrame OuterFortress (Cart) 01 — Operations & Maintenance (`v0.1.0`)

Operational runbook covering read-only upstream oracle boundaries, deterministic builds, zero-asset unit tests, 4-drive Sandbox E2E verification, DualSetup8 packaging, and 2-tier log forensics.

## 1. Install Topology & Clean Root Standard

- **Canonical Default Install Root (`%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\IronFrame-OuterFortress-Cart-01\`):**
  - Root (`Clean Root`): strictly `IronFrame-OuterFortress-Cart-01.exe` (`< 2 MB` single-file embedded shim), `THIRDPARTY_LICENSES.txt`, `settings.example.json`, and `.ready` (with `app\LICENSE`, `app\install-manifest.json`, `app\IronFrame-OuterFortress-Cart-01.recomp`, `app\IronFrame-OuterFortress-Cart-01-Uninstall.exe`; zero `AGENTS.md`).
  - `data\`: `data\rom\` (`NES-ME-USA.nes` / `.prg`), `data\config\settings*.json` (split-domain `SettingsDomainStore`), `data\cache\` (local AOT artifacts), `data\saves\`.
  - `logs\`: runtime diagnostic and heartbeat logs (`<= 15s` heartbeats).
- **Suite Layout (`%LOCALAPPDATA%\Programs\StreamItAll\reEngine\`):** `reEngine.exe` lives directly at `<suiteRoot>\` (never `<suiteRoot>\reEngine\`), `reCart` lives under `<suiteRoot>\engines\reCart\v0.1.3\` (with bundled `app\tools\{7z.exe,7z.dll,LICENSE-7ZIP.txt}`), and the game module lives under `<suiteRoot>\games\IronFrame-OuterFortress-Cart-01\`.
- **Upstream Boundary:** Reads upstream schemas read-only at release/recipe generation time (`RECOMP_SCHEMAS_DIR` or sibling `Research\...\Dev\schemas\`). Zero `Corpus\` files ever enter this repository.

## 2. Build, Solution & Zero-Warning Gate

- **Solution:** [`Dev\IronFrameCart01.slnx`](../IronFrameCart01.slnx) (`5` projects: `RecipeGen`, `Module`, `Launcher`, `Installer`, `Tests`).
- **Build Properties:** [`Dev\Directory.Build.props`](../Directory.Build.props) enforces `<TargetFramework>net8.0</TargetFramework>`, `<Deterministic>true</Deterministic>`, zero-PII `<PathMap>`, and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- **Pinned `Dev\lib\` Contracts:** Synced from `SharedLibs\Foundation\v0.5.5`, `SharedLibs\reEngine\v0.6.2`, and `SharedLibs\reCart\v0.1.3` (remember `.NET` assembly embeds inside `IronFrameCart01.Launcher` use `<Private>false</Private>` + `EmbeddedResource` + `AssemblyResolve`).

## 3. Verification Recipes

### A. Zero-Asset Isolated Unit Tests (`CI` & Pre-Package Gate)
```powershell
$env:APP_ISOLATED_TEST = "1"
dotnet test .\Dev\IronFrameCart01.slnx -c Release /warnaserror
```
Must pass all `50` tests across `RecipeGenTests`, `Cart01ModuleAndPackTests`, `LauncherContainmentAndPrecedenceTests`, `InstallerPillar4AndOnlineHttpTests`, and `PrelaunchRegressionTests` (covering `.7z` discovery via `ResolveSevenZipOverridePath` + `MediaContainerExtractor`, ephemeral temp cleanup, missing-tool failure paths, and clean-room `$C6B7` `$C2C6` lifting) with `0` skipped, `0` failed, and `0` warnings.

### B. 4-Drive Sandbox E2E & Differential Lockstep (`[S2-00]..[S2-09]`)
```powershell
Remove-Item Env:APP_ISOLATED_TEST -ErrorAction SilentlyContinue
powershell -NoProfile -ExecutionPolicy Bypass -File .\Dev\scripts\run_stage2_sandbox_e2e.ps1 -Version 0.1.0
```
- Uses `FOUNDATION_SANDBOX_ROOT` (`L` localapp / `R` roaming / `P` programdata / `T` temp + Desktop/StartMenu) while preserving core Windows OS env vars (`SystemDrive`, `ProgramData`, `ProgramFiles*`, `CommonProgramFiles`, `ALLUSERSPROFILE`, `PUBLIC`).
- Publishes automation-enabled test binaries strictly with `"-t:Rebuild,Publish"` and `-p:IronFrameTestAutomation=true` via [`Stage2PrelaunchProbes.ps1`](../scripts/Stage2PrelaunchProbes.ps1#L84-L377).
- Verifies Release builds reject `--automation-port` with `ExitCode = 2` on Launcher (`1,032,184 B`), Online Setup, and Offline Setup (`audit_installer.ps1` `DualSetup8 Passed: True, ExeCount: 2, FindingCount: 0`).
- Exercises `[S2-00]`..`[S2-09]` across [`run_stage2_sandbox_e2e.ps1`](../scripts/run_stage2_sandbox_e2e.ps1#L1-L334) and [`Stage2PrelaunchProbes.ps1`](../scripts/Stage2PrelaunchProbes.ps1):
  - `[S2-00]`..`[S2-05]`: real Wizard GUI automation (Offline Setup `Full` bundling `reEngine` + `reCart` + Online Setup `--offline-bundle`), `exebox-stream` (`udp://@127.0.0.1:5004`), adjacent verified Portable ZIP auto-discovery, clean default un-overridden Offline `Full` install (zero double nesting, zero non-coded directories), silent upgrade preserving `data\rom\` + `data\config\`, and `20,002`-step BYO AOT-vs-Oracle lockstep (`mismatchedSteps=0`) with clean-room `$C6B7` (`$C2C6` switchable bank) verification.
  - **`[S2-06]` Missing-ROM Gate & Corrupt `.7z` Probe** ([`Invoke-Stage2MissingAndCorruptMediaProbes`](../scripts/Stage2PrelaunchProbes.ps1#L126-L194)): verifies Media Gate opens `< 5s` (`2.68s`) when `data\rom\` is empty, rejects a corrupt `.7z` with a persisted error banner, and passes `Launch Demo / Test Cart`.
  - **`[S2-07]` Format Matrix & Live Static-AOT Gameplay** ([`Invoke-Stage2FormatAndLiveGameplayProbes`](../scripts/Stage2PrelaunchProbes.ps1#L195-L350)): boots Cold `.7z` with wiped `data\cache\` (`4.21s` live AOT overlay + boot) plus validates `.zip`, `.prg`, and `.nes` formats; advances past title screen via real `Enter` tap (`$C6B7/$C2C6` clean), confirms multi-frame `F3` HUD `w=623px` (`0 px` jitter), WAV `peakPcm=16286`, and `F5`/`F9` QuickSave/Load (all ephemeral ROM copies removed cleanly in `finally`).
  - **`[S2-08]` Live `F1` Settings Cross-Restart Persistence:** flips `Scale=4x`, `VSync=true`, and `Master Volume 70%` in the live `F1` drawer, quits via `System_QuitGame`, relaunches fresh in the same `--data-dir`, and confirms persisted values in `settings*.json` and live `F1` controls.
  - **`[S2-09]` Standalone Raw-ROM Clean-Room Guard** ([`Invoke-Stage2StandaloneRawRomGuardProbe`](../scripts/Stage2PrelaunchProbes.ps1#L351-L377)): launches raw `.nes` directly on `reCart.Player.exe` without a `.recomp` package (`2.18s`), verifying zero crash and interactive Clean-Room Media Gate guidance + `Select .recomp Module...`.

## 4. Single-Pass `DualSetup8` Packaging & Release Procedure

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Dev\scripts\build_release.ps1 -Version 0.1.0
```
Strict single-pass execution order:
1. Build & test solution cleanly under `APP_ISOLATED_TEST=1` (`/warnaserror`).
2. Pack `IronFrame-OuterFortress-Cart-01.recomp`, publish `< 2 MB` single-file `IronFrame-OuterFortress-Cart-01.exe`, stage `settings.example.json` + clean `Builds\v0.1.0\`.
3. Zip `Builds\v0.1.0\` -> `Releases\v0.1.0\IronFrame-OuterFortress-Cart-01-v0.1.0-win-x64-Portable.zip` and stamp exact SHA-256 + byte length into temp Online `installer.manifest.json`.
4. Compile Online Setup (`IronFrame-OuterFortress-Cart-01-Setup-v0.1.0-win-x64.exe` pointing to `StreamItAllGit/IronFrame-OuterFortress-Cart-01-Recomp/releases/download/v0.1.0/`).
5. Zip `Builds\v0.1.0\` -> temporary `payload.zip` and compile self-contained Offline Setup (`IronFrame-OuterFortress-Cart-01-Offline-Setup-v0.1.0-win-x64.exe`).
6. Authenticode sign Launcher + Offline Setup + Online Setup cleanly when certificate configured, assemble 8-file `Releases\v0.1.0\`, NTFS-hardlink `Releases\Latest\`, generate `Releases\IronFrame-OuterFortress-Cart-01-v0.1.0-win-x64.7z` + `Latest.7z`, sync central `Releases\` junctions (`IronFrame-OuterFortress-Cart-01-Recomp`), run `audit_installer.ps1 -RequireDualInstaller -VerifyChecksums`, and archive `Archive\IronFrame-OuterFortress-Cart-01-Recomp-v0.1.0-src.7z`.

## 5. Logs & 2-Tier Forensics

On any runtime or installer incident, inspect the preceding 20–50 lines across both canonical log roots before making changes:
1. **Installed Runtime Logs (Read-Only):** `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\IronFrame-OuterFortress-Cart-01\logs\` (or `<SandboxLocalAppData>\Programs\...` during Sandbox E2E).
2. **Developer & Test Runner Logs:** `Dev\logs\` (including `stage2_e2e\`, build transcripts, and crash bundles).
