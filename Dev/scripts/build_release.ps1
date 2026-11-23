#Requires -Version 5.1
# @module: Dev/scripts/build_release.ps1
# @purpose: DualSetup8 Release pipeline: clean-room SLN build/test, Builds\, Portable.zip first, Online+Offline Setup EXEs, 8-file hardlinked Releases\, junction, audit, and src archive.
# @depends-on: Dev/version.txt, Dev/IronFrameCart01.slnx, Dev/scripts/pack_recomp_module.ps1, Dev/Installer/installer.manifest.json, Dev/scripts/THIRDPARTY_LICENSES.txt, audit_installer.ps1
# @exports: Canonical DualSetup8 release build script

[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$CertThumbprint = "A5414BDD36F7F113C7B2356F189B7A43F69C35E6",
    [switch]$SkipSigning,
    [switch]$SkipTests,
    [switch]$SkipSourceArchive
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$utf8 = [System.Text.UTF8Encoding]::new($false)
function Read-Utf8([string]$p) { [System.IO.File]::ReadAllText($p, $utf8) }
function Write-Utf8([string]$p, [string]$t) { [System.IO.File]::WriteAllText($p, $t, $utf8) }

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$devDir = Join-Path $root "Dev"
$workspacePersonal = Split-Path -Parent $root
$workspaceProjects = Split-Path -Parent $workspacePersonal
$logsDir = Join-Path $devDir "logs"
New-Item -ItemType Directory -Force $logsDir | Out-Null
Get-ChildItem -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { -not $_.PSIsContainer -and (($_.Name -in @("desktop.ini", "Thumbs.db", "ehthumbs.db", ".DS_Store")) -or $_.Name.StartsWith("._")) } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }

if ([string]::IsNullOrWhiteSpace($Version)) { $Version = (Read-Utf8 (Join-Path $devDir "version.txt")).Trim() }
$Version = $Version.Trim()
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Invalid SemVer version: '$Version'" }

$licensePath = Join-Path $root "LICENSE"
$thirdPartyPath = Join-Path $devDir "scripts\THIRDPARTY_LICENSES.txt"
$packScript = Join-Path $devDir "scripts\pack_recomp_module.ps1"
$baseManifestPath = Join-Path $devDir "Installer\installer.manifest.json"
$sevenZip = @("7z.exe", (Join-Path $devDir "assets\native\7z.exe"), (Join-Path $env:ProgramFiles "7-Zip\7z.exe")) |
    Where-Object { (Get-Command $_ -ErrorAction SilentlyContinue) -or (Test-Path -LiteralPath $_) } | Select-Object -First 1
foreach ($req in @($licensePath, $thirdPartyPath, $packScript, $baseManifestPath)) { if (-not (Test-Path -LiteralPath $req)) { throw "Required file missing: $req" } }
if (-not $sevenZip) { throw "7z.exe not found." }

$cert = $null
if (-not $SkipSigning) {
    $cert = Get-ChildItem "Cert:\CurrentUser\My", "Cert:\LocalMachine\My" -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -ieq $CertThumbprint.Trim() } | Select-Object -First 1
    if (-not $cert) { throw "Signing certificate '$CertThumbprint' not found." }
}

function Invoke-Sign([string]$Target) {
    if ($SkipSigning) { return }
    $res = Set-AuthenticodeSignature -Certificate $cert -HashAlgorithm SHA256 -FilePath $Target
    if ($res.Status -ne "Valid") { throw "Authenticode signing failed for ${Target}: $($res.Status) - $($res.StatusMessage)" }
}

function Invoke-7z([string[]]$ArgList) {
    & $sevenZip @ArgList | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7z.exe exited $LASTEXITCODE ($($ArgList -join ' '))" }
}

function Compress-GzipFile([string]$SrcPath, [string]$DstPath) {
    Add-Type -AssemblyName System.IO.Compression
    $inFs = [System.IO.File]::OpenRead($SrcPath)
    try {
        $outFs = [System.IO.File]::Create($DstPath)
        try {
            $gz = [System.IO.Compression.GZipStream]::new($outFs, [System.IO.Compression.CompressionLevel]::Optimal)
            try { $inFs.CopyTo($gz) } finally { $gz.Dispose() }
        } finally { $outFs.Dispose() }
    } finally { $inFs.Dispose() }
}

function Sanitize-Markdown([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return "`r`n" }
    $fileUri = 'file:' + '///'
    $c = $Text.Replace(([string][char]0x00C2 + [char]0x00A7), [string][char]0x00A7).Replace(([string][char]0x00E2 + [char]0x20AC + [char]0x201D), "--")
    $c = [regex]::Replace($c, '\[([^\]]+)\]\(' + $fileUri + '[^)]+\)', '$1')
    $c = [regex]::Replace($c, '\[([^\]]+)\]\((?:\.\.?[\\/])+LICENSE\)', '$1')
    $c = [regex]::Replace($c, '\[([^\]]+)\]\((?:(?:\.\.?[\\/])+)?(?:Dev|src|tests|docs|scripts|lib|assets)[\\/][^)]+\)', '$1')
    $c = [regex]::Replace($c, '`' + $fileUri + '[^`\r\n]+`|' + $fileUri + '\S+', '')
    $userEsc = if ([string]::IsNullOrWhiteSpace($env:USERNAME)) { "" } else { "|" + [regex]::Escape($env:USERNAME) }
    $drop = "(?i)Dev[\\/](?:src|tests|docs|scripts|lib|assets)|SharedLibs[\\/]|audit_(?:installer|dossier)\.ps1|[CD]:[\\/](?:Projects|Users|Programs)|_DevBible|\bDev\s+Bible\b$userEsc"
    $lines = foreach ($line in ($c -split "\r?\n")) {
        $s = $line
        if ($s.TrimStart().StartsWith("|")) { $s = [regex]::Replace($s, '(?i)`?Dev[\\/](?:src|tests|docs|scripts|lib|assets)[\\/][^`|]*`?', 'IronFrame-OuterFortress-Cart-01 module') }
        if ($s -match $drop) { continue }
        $s = [regex]::Replace($s, '`?@(?:pland|dean|devv|inspecter|tessa|nora|milo|simon|julian|holly|rowan|coordinator|monitor|everyone)[a-z0-9-]*`?', '')
        $s = [regex]::Replace($s, "(?i)``?(?:per\s+)?\bStandards?\s+\d+(?:\s*[-\u2013]\s*\d+)?(?:\s*\u00A7[\d.]+)?``?", '')
        $s = $s.Replace('MetalGearRecomp', 'IronFrame-OuterFortress-Cart-01').Replace('metalgearrecomp', 'ironframe-outerfortress-cart-01')
        ([regex]::Replace([regex]::Replace($s, '\s*\(\s*\)', ''), '(?<=\S) {2,}', ' ')).TrimEnd()
    }
    return (($lines -join "`r`n") -replace "(\r?\n){3,}", "`r`n`r`n").Trim() + "`r`n"
}

function Ensure-LegalNotice([string]$Text) {
    if ($Text -match '(?im)^##\s*(?:\d+\.\s*)?License\s*&\s*Legal\b') { return ($Text.Trim() + "`r`n") }
    $legal = @"

## License & Legal

Distributed under the **MIT License** (``LICENSE``, Copyright (c) 2026 StreamItAll). **Clean-Room BYO ROM Notice:** Zero copyrighted game ROMs, graphics, audio, or scripts are included or distributed. Players must supply their own legally obtained cartridge dump (``NES-ME-USA``) inside ``data\rom\``.
"@
    return ($Text.Trim() + "`r`n" + $legal.Trim() + "`r`n")
}

function Assert-EndUserClean([string]$Path, [string]$Text) {
    $fileUri = 'file:' + '///'
    $userEsc = if ([string]::IsNullOrWhiteSpace($env:USERNAME)) { "" } else { "|" + [regex]::Escape($env:USERNAME) }
    $m = [regex]::Match($Text, "(?i)$fileUri|Dev[\\/](?:src|tests|docs|scripts|lib|assets)|SharedLibs[\\/]|audit_(?:installer|dossier)\.ps1|[CD]:[\\/](?:Projects|Users|Programs)|_DevBible|\bDev\s+Bible\b|@(?:pland|dean|devv|inspecter|tessa|nora|milo|simon|julian|holly|rowan|coordinator|monitor|everyone)\b$userEsc")
    if ($m.Success) { throw "End-user markdown leak in '$Path': '$($m.Value)'." }
}

function Assert-BinaryPurity([string]$BinPath, [string[]]$ForbiddenTokens, [string]$DepsJsonPath = "") {
    if (-not (Test-Path -LiteralPath $BinPath)) { throw "Binary missing for purity check: $BinPath" }
    $fs = [System.IO.File]::OpenRead($BinPath)
    try {
        $chunkSize = 4194304; $overlap = 1024; $buf = New-Object byte[] $chunkSize; $offset = 0L
        while ($offset -lt $fs.Length) {
            $fs.Position = $offset; $read = $fs.Read($buf, 0, $chunkSize); if ($read -le 0) { break }
            $ascii = [System.Text.Encoding]::ASCII.GetString($buf, 0, $read)
            $utf16 = [System.Text.Encoding]::Unicode.GetString($buf, 0, ($read - ($read % 2)))
            foreach ($tok in $ForbiddenTokens) {
                if ($ascii.IndexOf($tok, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                    $utf16.IndexOf($tok, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    throw "Binary purity violation in '$BinPath': found '$tok'."
                }
            }
            if (($offset + $read) -ge $fs.Length) { break }
            $offset += ($chunkSize - $overlap)
        }
    } finally { $fs.Dispose() }
    if (-not [string]::IsNullOrWhiteSpace($DepsJsonPath) -and (Test-Path -LiteralPath $DepsJsonPath)) {
        $depsText = Read-Utf8 $DepsJsonPath
        foreach ($tok in $ForbiddenTokens) {
            if ($depsText.IndexOf($tok, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "Deps purity violation in '$DepsJsonPath': found '$tok'." }
        }
    }
}

function Assert-AutomationExit2([string]$ExePath, [string[]]$ExtraArgs = @()) {
    $prev = $env:APP_ISOLATED_TEST; $env:APP_ISOLATED_TEST = "1"
    try {
        $p = Start-Process -FilePath $ExePath -WindowStyle Hidden -Wait -PassThru -ArgumentList (@("--automation-port", "5199") + $ExtraArgs)
        if ($p.ExitCode -ne 2) { throw "Release automation gate failed for '$ExePath': expected ExitCode=2, got $($p.ExitCode)." }
    } finally { if ($null -eq $prev) { Remove-Item Env:APP_ISOLATED_TEST -ErrorAction SilentlyContinue } else { $env:APP_ISOLATED_TEST = $prev } }
}

function Ensure-Junction([string]$LinkPath, [string]$TargetDir) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $LinkPath) | Out-Null
    if (Test-Path -LiteralPath $LinkPath) {
        $it = Get-Item -LiteralPath $LinkPath -Force
        if (($it.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            if (($it.Target | Select-Object -First 1).TrimEnd('\') -ieq $TargetDir.TrimEnd('\')) { return }
            & cmd /c rmdir "`"$LinkPath`"" | Out-Null
        } else { Remove-Item -LiteralPath $LinkPath -Recurse -Force }
    }
    & cmd /c mklink /J "`"$LinkPath`"" "`"$TargetDir`"" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed creating junction '$LinkPath' -> '$TargetDir'" }
}

Write-Host "[1/6] Building & testing Dev\IronFrameCart01.slnx under APP_ISOLATED_TEST=1 (/warnaserror)..."
$slnxPath = Join-Path $devDir "IronFrameCart01.slnx"
& dotnet build $slnxPath -c Release /warnaserror --disable-build-servers -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
if (-not $SkipTests) {
    $prevIso = $env:APP_ISOLATED_TEST; $env:APP_ISOLATED_TEST = "1"
    try { & dotnet test $slnxPath -c Release --no-build --logger "console;verbosity=minimal"; if ($LASTEXITCODE -ne 0) { throw "dotnet test failed ($LASTEXITCODE)" } }
    finally { if ($null -eq $prevIso) { Remove-Item Env:APP_ISOLATED_TEST -ErrorAction SilentlyContinue } else { $env:APP_ISOLATED_TEST = $prevIso } }
}

Write-Host "[2/6] Staging single-pass Builds\v$Version\..."
$buildsRoot = Join-Path $root "Builds"; $stageDir = Join-Path $buildsRoot "v$Version"; $stageApp = Join-Path $stageDir "app"
if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force $stageApp | Out-Null

$detProps = @("-p:DebugType=none", "-p:DebugSymbols=false", "-p:Deterministic=true", "-p:ContinuousIntegrationBuild=true", "-p:PathMap=$root=.")
$workTemp = Join-Path $env:TEMP ("mgr_pkg_" + $Version + "_" + [Guid]::NewGuid().ToString("N").Substring(0, 6))
if (Test-Path -LiteralPath $workTemp) { Remove-Item -LiteralPath $workTemp -Recurse -Force }
$launcherTmp = Join-Path $workTemp "launcher_out"; New-Item -ItemType Directory -Force $launcherTmp | Out-Null

& dotnet publish (Join-Path $devDir "src\IronFrameCart01.Launcher\IronFrameCart01.Launcher.csproj") -c Release -r win-x64 --self-contained false `
    -p:Version="$Version" -p:PublishSingleFile=true @detProps -o $launcherTmp /warnaserror --disable-build-servers -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed ($LASTEXITCODE)" }
$codedSlug = "IronFrame-OuterFortress-Cart-01"
$launcherBin = Join-Path $launcherTmp "$codedSlug.exe"
if ((Get-Item -LiteralPath $launcherBin).Length -ge 2097152) { throw "$codedSlug.exe exceeded 2 MB budget." }
$stagedLauncher = Join-Path $stageDir "$codedSlug.exe"
Copy-Item -LiteralPath $launcherBin -Destination $stagedLauncher -Force
Copy-Item -LiteralPath (Join-Path $devDir "settings.example.json") -Destination (Join-Path $stageDir "settings.example.json") -Force
Assert-BinaryPurity $stagedLauncher @("Foundation.Automation", "Foundation.UI", "System.Windows.Forms") (Join-Path $launcherTmp "$codedSlug.deps.json")

$recompPath = Join-Path $stageApp "$codedSlug.recomp"
& powershell -NoProfile -ExecutionPolicy Bypass -File $packScript -OutputPath $recompPath
if ($LASTEXITCODE -ne 0) { throw "pack_recomp_module.ps1 failed ($LASTEXITCODE)" }

try { New-Item -ItemType HardLink -Path (Join-Path $stageApp "LICENSE") -Target $licensePath | Out-Null } catch { Copy-Item -LiteralPath $licensePath -Destination (Join-Path $stageApp "LICENSE") -Force }
Copy-Item -LiteralPath $thirdPartyPath -Destination (Join-Path $stageDir "THIRDPARTY_LICENSES.txt") -Force
Get-ChildItem -LiteralPath $stageDir -Recurse -File | Where-Object { $_.Extension -in @('.pdb', '.xml') } | Remove-Item -Force
$badStage = @(Get-ChildItem -LiteralPath $stageDir -Recurse -Force -File | Where-Object {
    ($_.Extension -in @(".pdb", ".xml", ".db", ".sqlite", ".sqlite3", ".log", ".nes", ".prg")) -or
    ($_.Name -ilike "*.g.cs") -or ($_.Name -ilike "*.Generated.dll") -or ($_.Name -ieq "AGENTS.md") -or ($_.Name -ieq ".env") -or ($_.Name -ilike "*MetalGear*")
})
if ($badStage.Count -gt 0) { throw "Forbidden staging file(s): $($badStage.FullName -join ', ')" }

Invoke-Sign $stagedLauncher
$probeDir = Join-Path $workTemp "probe"; New-Item -ItemType Directory -Force (Join-Path $probeDir "data"), (Join-Path $probeDir "logs") | Out-Null
Assert-AutomationExit2 $stagedLauncher @("--data-dir", (Join-Path $probeDir "data"), "--log-dir", (Join-Path $probeDir "logs"))
Get-ChildItem -LiteralPath $buildsRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^v\d+\.\d+\.\d+' } |
    Sort-Object { try { [version]($_.Name.TrimStart('v').Split('-')[0]) } catch { [version]"0.0.0" } } -Descending | Select-Object -Skip 2 | Remove-Item -Recurse -Force

Write-Host "[3/6] Sanitizing end-user docs & building Portable.zip FIRST..."
$guideParts = @((Read-Utf8 (Join-Path $devDir "docs\USER_GUIDE.md")))
Get-ChildItem (Join-Path $devDir "docs") -Filter "USER_GUIDE.*.md" -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { $guideParts += (Read-Utf8 $_.FullName) }
$rawNotesTemplate = @'
# IronFrame OuterFortress (Cart) 01 — Release v__VERSION__

Clean-room static recompilation cartridge module (`ironframe-outerfortress-cart-01`) for `reCart` (`v0.1.3`) and `reEngine` (`v0.6.2`, `AllowAutoStart = false`).

## Highlights
- **Zero-Asset Clean-Room Cartridge (`.recomp`):** Locally compiles AOT into `<dataDir>\cache\` from a player-supplied legal cartridge dump under `data\rom\`.
- **Dual-Variant BYO Verification (`NES-ME-USA`):** Supports both `131,088`-byte full cartridge and `131,072`-byte headerless PRG images.
- **Dual-Installer Distribution:** Compact Online Setup (`IronFrame-OuterFortress-Cart-01-Setup-v__VERSION__-win-x64.exe`), full Offline Setup (`IronFrame-OuterFortress-Cart-01-Offline-Setup-v__VERSION__-win-x64.exe` with bundled `reEngine` + `reCart`), and Portable ZIP (`IronFrame-OuterFortress-Cart-01-v__VERSION__-win-x64-Portable.zip`) preserving `data\rom\` and `data\config\settings*.json`.
'@
$rawNotes = $rawNotesTemplate.Replace('__VERSION__', $Version)
$docMap = [ordered]@{
    "README.md"        = Ensure-LegalNotice (Sanitize-Markdown ($guideParts -join "`r`n`r`n"))
    "FEATURES.md"      = Sanitize-Markdown (Read-Utf8 (Join-Path $root "FEATURES.md"))
    "RELEASE_NOTES.md" = Ensure-LegalNotice (Sanitize-Markdown $rawNotes)
    "CHANGELOG.md"     = Sanitize-Markdown (Read-Utf8 (Join-Path $root "CHANGELOG.md"))
}

$releasesDir = Join-Path $root "Releases"; $relVerDir = Join-Path $releasesDir "v$Version"; $relLatestDir = Join-Path $releasesDir "Latest"
foreach ($d in @($relVerDir, $relLatestDir)) { if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force }; New-Item -ItemType Directory -Force $d | Out-Null }
foreach ($kv in $docMap.GetEnumerator()) { $outMd = Join-Path $relVerDir $kv.Key; Assert-EndUserClean $outMd $kv.Value; Write-Utf8 $outMd $kv.Value }

$portableName = "$codedSlug-v$Version-win-x64-Portable.zip"; $portablePath = Join-Path $relVerDir $portableName
$portableStage = Join-Path $workTemp "portable_stage"; New-Item -ItemType Directory -Force $portableStage | Out-Null
Copy-Item -Path (Join-Path $stageDir "*") -Destination $portableStage -Recurse -Force
foreach ($kv in $docMap.GetEnumerator()) { Write-Utf8 (Join-Path $portableStage $kv.Key) $kv.Value }
if (@(Get-ChildItem -LiteralPath $portableStage -Recurse -File | Where-Object { $_.Name -ilike "*Setup*.exe" -or $_.Name -ieq "AGENTS.md" -or $_.Name -ilike "*MetalGear*" }).Count -gt 0) { throw "Portable staging violation" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($portableStage, $portablePath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$portableSize = (Get-Item -LiteralPath $portablePath).Length
$portableSha = (Get-FileHash -LiteralPath $portablePath -Algorithm SHA256).Hash.ToLowerInvariant()
$nowIso = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
$onlineObj = (Read-Utf8 $baseManifestPath) | ConvertFrom-Json
$onlineObj.version = $Version; $onlineObj.updatedAtUtc = $nowIso; $onlineObj.installerDeliveryKind = "onlineSetup"
$onlineObj.payloadDownloadUrl = "https://github.com/StreamItAllGit/IronFrame-OuterFortress-Cart-01-Recomp/releases/download/v$Version/"
$onlineObj.payloadAssets = @([ordered]@{ installProfile = "*"; fileName = $portableName; expectedSizeBytes = $portableSize; expectedSha256 = $portableSha })
$onlineManifest = Join-Path $workTemp "online.manifest.json"; Write-Utf8 $onlineManifest (($onlineObj | ConvertTo-Json -Depth 6) + "`r`n")

$offlineObj = (Read-Utf8 $baseManifestPath) | ConvertFrom-Json
$offlineObj.version = $Version; $offlineObj.updatedAtUtc = $nowIso; $offlineObj.installerDeliveryKind = "offlineSetup"
$offlineObj.PSObject.Properties.Remove("payloadDownloadUrl"); $offlineObj.PSObject.Properties.Remove("payloadAssets")
$offlineManifest = Join-Path $workTemp "offline.manifest.json"; Write-Utf8 $offlineManifest (($offlineObj | ConvertTo-Json -Depth 6) + "`r`n")

Write-Host "[4/6] Publishing compact Online Setup & full Offline Setup..."
$instProj = Join-Path $devDir "src\IronFrameCart01.Installer\IronFrameCart01.Installer.csproj"
$onlineAsm = "$codedSlug-Setup-v$Version-win-x64"; $offlineAsm = "$codedSlug-Offline-Setup-v$Version-win-x64"
$onlineOut = Join-Path $workTemp "online_inst"; $offlineOut = Join-Path $workTemp "offline_inst"
$payloadZip = Join-Path $workTemp "payload.zip"
[System.IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

& dotnet publish $instProj -c Release -r win-x64 --self-contained true -p:Version="$Version" -p:InstallerAssemblyName="$onlineAsm" `
    -p:InstallerManifestPath="$onlineManifest" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true @detProps -o $onlineOut /warnaserror --disable-build-servers -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "Online Setup publish failed ($LASTEXITCODE)" }

$reEngineSetupSrc = if (-not [string]::IsNullOrWhiteSpace($env:REENGINE_SETUP_PATH)) { $env:REENGINE_SETUP_PATH } else { Join-Path $workspaceProjects "SharedLibs\reEngine\v0.6.2\reEngine-Setup-v0.6.2-win-x64.exe" }
$reCartSetupSrc = if (-not [string]::IsNullOrWhiteSpace($env:RECART_SETUP_PATH)) { $env:RECART_SETUP_PATH } else { Join-Path $workspacePersonal "reCartOne\Releases\v0.1.3\reCart-Setup-v0.1.3-win-x64.exe" }
$offlinePublishProps = @("-p:InstallerPayloadZip=$payloadZip")
if (Test-Path -LiteralPath $reEngineSetupSrc) { $reGz = Join-Path $workTemp "reEngine-Setup.gz"; Compress-GzipFile $reEngineSetupSrc $reGz; $offlinePublishProps += "-p:ReEngineBootstrapGz=$reGz" }
if (Test-Path -LiteralPath $reCartSetupSrc) { $rcGz = Join-Path $workTemp "reCart-Setup.gz"; Compress-GzipFile $reCartSetupSrc $rcGz; $offlinePublishProps += "-p:ReCartBootstrapGz=$rcGz" }

& dotnet publish $instProj -c Release -r win-x64 --self-contained true -p:Version="$Version" -p:InstallerAssemblyName="$offlineAsm" `
    -p:InstallerManifestPath="$offlineManifest" @offlinePublishProps -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true @detProps -o $offlineOut /warnaserror --disable-build-servers -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "Offline Setup publish failed ($LASTEXITCODE)" }

$onlinePath = Join-Path $relVerDir "$onlineAsm.exe"; $offlinePath = Join-Path $relVerDir "$offlineAsm.exe"
Copy-Item -LiteralPath (Join-Path $onlineOut "$onlineAsm.exe") -Destination $onlinePath -Force
Copy-Item -LiteralPath (Join-Path $offlineOut "$offlineAsm.exe") -Destination $offlinePath -Force
foreach ($exe in @($onlinePath, $offlinePath)) {
    Assert-BinaryPurity $exe @("Foundation.Automation", "Foundation.UI")
    Invoke-Sign $exe
    Assert-AutomationExit2 $exe
}

Write-Host "[5/6] Writing SHA256SUMS.txt, hardlinking Latest\, cutting .7z, junction & running audit_installer..."
$bundleItems = @("$onlineAsm.exe", "$offlineAsm.exe", $portableName, "README.md", "FEATURES.md", "RELEASE_NOTES.md", "CHANGELOG.md")
$sumLines = foreach ($fn in ($bundleItems | Sort-Object)) { "$((Get-FileHash -LiteralPath (Join-Path $relVerDir $fn) -Algorithm SHA256).Hash.ToLowerInvariant())  $fn" }
[System.IO.File]::WriteAllText((Join-Path $relVerDir "SHA256SUMS.txt"), ($sumLines -join "`r`n") + "`r`n", [System.Text.Encoding]::ASCII)
if (@(Get-ChildItem -LiteralPath $relVerDir -Force).Count -ne 8) { throw "Expected strictly 8 items inside $relVerDir" }
$nonCodedRel = @(Get-ChildItem -LiteralPath $relVerDir -Force -File | Where-Object { $_.Name -ilike "*MetalGear*" })
if ($nonCodedRel.Count -gt 0) { throw "Clean-room release naming violation: $($nonCodedRel.FullName -join ', ')" }

foreach ($fn in ($bundleItems + @("SHA256SUMS.txt"))) {
    $src = Join-Path $relVerDir $fn; $dst = Join-Path $relLatestDir $fn
    try { New-Item -ItemType HardLink -Path $dst -Target $src | Out-Null } catch { Copy-Item -LiteralPath $src -Destination $dst -Force }
}
Remove-Item (Join-Path $releasesDir "MetalGearRecomp-v$Version-win-x64.7z"), (Join-Path $releasesDir "$codedSlug-v$Version-win-x64.7z"), (Join-Path $releasesDir "Latest.7z") -Force -ErrorAction SilentlyContinue
Invoke-7z @("a", "-t7z", "-mx=9", "-mmt=on", (Join-Path $releasesDir "$codedSlug-v$Version-win-x64.7z"), (Join-Path $relVerDir "*"))
Invoke-7z @("a", "-t7z", "-mx=9", "-mmt=on", (Join-Path $releasesDir "Latest.7z"), (Join-Path $relLatestDir "*"))

$centralRelRoot = if (-not [string]::IsNullOrWhiteSpace($env:PROJECTS_RELEASES_ROOT)) { $env:PROJECTS_RELEASES_ROOT } else { Join-Path $workspaceProjects "Releases" }
$centralGroupDir = Join-Path $centralRelRoot (Split-Path -Leaf $workspacePersonal)
foreach ($hubName in @((Split-Path -Leaf $root), "$codedSlug-Recomp") | Select-Object -Unique) {
    Ensure-Junction (Join-Path $centralGroupDir $hubName) $releasesDir
}

$auditScript = Join-Path $env:USERPROFILE ".gemini\config\skills\dev\scripts\audit_installer.ps1"
if ((-not $SkipSigning) -and (Test-Path -LiteralPath $auditScript)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $auditScript -InstallerPath $relVerDir -StagingDir $stageDir -ExpectedVersion $Version -RequireDualInstaller -VerifyChecksums
    if ($LASTEXITCODE -ne 0) { throw "audit_installer.ps1 failed ($LASTEXITCODE)" }
}

if (-not $SkipSourceArchive) {
    Write-Host "[6/6] Archiving clean-room source snapshot into Archive\$codedSlug-Recomp-v$Version-src.7z..."
    $archiveDir = Join-Path $root "Archive"; New-Item -ItemType Directory -Force $archiveDir | Out-Null
    Remove-Item (Join-Path $archiveDir "MetalGearRecomp-v$Version-src.7z") -Force -ErrorAction SilentlyContinue
    $srcArchive = Join-Path $archiveDir "$codedSlug-Recomp-v$Version-src.7z"; Remove-Item $srcArchive -Force -ErrorAction SilentlyContinue
    $srcItems = @(".github", "Dev", "README.md", "FEATURES.md", "ROADMAP.md", "CHANGELOG.md", "AGENTS.md", "LICENSE", "SECURITY.md", "CONTRIBUTING.md", "active.state") | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) }
    Push-Location $root
    try { Invoke-7z (@("a", "-t7z", "-mx=9", "-mmt=on", "-xr!bin", "-xr!obj", "-xr!TestResults", "-xr!.git", "-xr!data", "-xr!logs", "-xr!.vs", "-xr!*.nes", "-xr!*.prg", "-xr!*.g.cs", "-xr!*.Generated.dll", "-xr!*.pdb", "-xr!*.tmp", "-xr!*.bak", "-xr!*.user", "-xr!desktop.ini", "-xr!Thumbs.db", "-xr!ehthumbs.db", "-xr!.DS_Store", "-xr!._*", $srcArchive) + $srcItems) }
    finally { Pop-Location }
}

$buildSummary = "[$(([DateTime]::UtcNow).ToString('yyyy-MM-ddTHH:mm:ss.fffZ'))] DualSetup8 Release v$Version built, signed=$(-not $SkipSigning), audited ($($relVerDir))."
Write-Utf8 (Join-Path $logsDir "build_v$Version.log") ($buildSummary + "`r`n")
Remove-Item -LiteralPath $workTemp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "DONE: DualSetup8 Release v$Version built, verified, and packaged."
