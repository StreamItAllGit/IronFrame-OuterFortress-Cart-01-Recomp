#Requires -Version 5.1
# @module: Dev/scripts/run_stage2_sandbox_e2e.ps1
# @purpose: Stage 2 4-drive sandbox E2E: Release ExitCode=2 gate, Dual Wizard UI + exebox-stream UDP 5004 screenshots, upgrade preservation, Launcher --verify/--dry-run, and optional BYO AOT+20k lockstep.
# @depends-on: Dev/version.txt, Foundation/v0.5.5/tools/ExeboxStreamHelpers.ps1, Builds/, Releases/
# @exports: Stage 2 sandbox E2E harness

[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$SnapshotDir = "",
    [string]$SandboxRoot = "",
    [string]$ByoRomPath = "",
    [switch]$SkipInstallerSandbox,
    [switch]$SkipUiAutomation,
    [switch]$SkipReleaseGate,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$devDir = Join-Path $root "Dev"
$workspacePersonal = Split-Path -Parent $root
$workspaceProjects = Split-Path -Parent $workspacePersonal
$logsDir = Join-Path $devDir "logs"
New-Item -ItemType Directory -Force $logsDir | Out-Null
if ([string]::IsNullOrWhiteSpace($ByoRomPath) -and -not [string]::IsNullOrWhiteSpace($env:MGR_BYO_ROM_PATH)) { $ByoRomPath = $env:MGR_BYO_ROM_PATH.Trim() }
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = ([System.IO.File]::ReadAllText((Join-Path $devDir "version.txt"))).Trim() }
if ([string]::IsNullOrWhiteSpace($SnapshotDir)) { $SnapshotDir = Join-Path $env:TEMP ("mgr_stage2_snaps_" + $Version) }
New-Item -ItemType Directory -Force $SnapshotDir | Out-Null

$exeboxHelpers = if (-not [string]::IsNullOrWhiteSpace($env:EXEBOX_HELPERS_PATH)) { $env:EXEBOX_HELPERS_PATH } else { Join-Path $workspaceProjects "SharedLibs\Foundation\v0.5.5\tools\ExeboxStreamHelpers.ps1" }
if (-not (Test-Path -LiteralPath $exeboxHelpers)) { throw "Missing ExeboxStreamHelpers.ps1 at $exeboxHelpers (override via EXEBOX_HELPERS_PATH)." }
. $exeboxHelpers
$stage2PrelaunchProbes = Join-Path $PSScriptRoot "Stage2PrelaunchProbes.ps1"
if (-not (Test-Path -LiteralPath $stage2PrelaunchProbes)) { throw "Missing Stage2PrelaunchProbes.ps1 at $stage2PrelaunchProbes" }
. $stage2PrelaunchProbes
$preflight = Initialize-ExeboxTestViewerPreflight -UdpPort 5004
Write-Host ("[S2-00] Desktop='{0}' IsExebox={1} ViewerActive={2}" -f $preflight.desktopName, $preflight.isExebox, $preflight.viewerActive)

$sbBase = Join-Path ([System.IO.Path]::GetPathRoot($root)) "_mgr_sandbox_tmp"
$codedSlug = "IronFrame-OuterFortress-Cart-01"
$sandbox = if ($SandboxRoot) { $SandboxRoot } else { Join-Path $sbBase ("mgr_s2_box_" + [Guid]::NewGuid().ToString("N").Substring(0, 8)) }
$sbC = Join-Path $sandbox "C"; $sbD = Join-Path $sandbox "D"; $sbRem = Join-Path $sandbox "Rem"; $sbDisc = Join-Path $sandbox "Disc"
$sbProf = Join-Path $sbC "Users\Sandbox"; $sbLocal = Join-Path $sbProf "AppData\Local"; $sbTemp = Join-Path $sbLocal "Temp"
$sbSuite = Join-Path $sbD "Programs\StreamItAll\reEngine"; $sbGames = Join-Path $sbSuite "games"
$sbOffGame = Join-Path $sbGames $codedSlug; $sbOnGame = Join-Path $sbD "Programs\OnlineTest\StreamItAll\reEngine\games\$codedSlug"
$sbAdjGame = Join-Path $sbD "Programs\AdjacentTest\StreamItAll\reEngine\games\$codedSlug"
$sbLogs = Join-Path $sandbox "logs"
New-Item -ItemType Directory -Force $sbC, $sbD, $sbRem, $sbDisc, $sbProf, $sbLocal, $sbTemp, (Join-Path $sbProf "Desktop"), (Join-Path $sbProf "AppData\Roaming\Microsoft\Windows\Start Menu\Programs"), $sbSuite, $sbGames, $sbLogs, (Join-Path $sandbox "registry"), (Join-Path $sandbox "locks") | Out-Null
[System.IO.File]::WriteAllText((Join-Path $sandbox "registry\registry.hive.json"), '{"schemaVersion":1,"registryHives":{"HKCU":{},"HKLM":{},"HKCR":{}}}')

$sysKeys = @("SYSTEMROOT", "WINDIR", "COMSPEC", "PATH", "PATHEXT", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "OS", "DOTNET_ROOT", "DOTNET_CLI_TELEMETRY_OPTOUT", "SystemDrive", "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "CommonProgramFiles", "CommonProgramFiles(x86)", "ALLUSERSPROFILE", "PUBLIC")
function Build-SandboxEnv([string]$RootDir = $sandbox, [string]$CDrive = $sbC, [string]$DDrive = $sbD, [string]$ProfDir = $sbProf, [string]$LocalDir = $sbLocal, [string]$TmpDir = $sbTemp) {
    $map = @{}
    foreach ($k in $sysKeys) { $v = [Environment]::GetEnvironmentVariable($k); if (-not [string]::IsNullOrWhiteSpace($v)) { $map[$k] = $v } }
    $map["FOUNDATION_SANDBOX_ROOT"] = $RootDir; $map["SANDBOX_C_DRIVE"] = $CDrive; $map["SANDBOX_D_DRIVE"] = $DDrive
    $map["SANDBOX_REMOVABLE_DRIVE"] = $sbRem; $map["SANDBOX_DISC_DRIVE"] = $sbDisc; $map["FOUNDATION_SANDBOX_PROFILE_DIR"] = $ProfDir
    $map["USERPROFILE"] = $ProfDir; $map["HOME"] = $ProfDir; $map["LOCALAPPDATA"] = $LocalDir
    $map["APPDATA"] = Join-Path $ProfDir "AppData\Roaming"; $map["TEMP"] = $TmpDir; $map["TMP"] = $TmpDir; $map["FOUNDATION_SUPPRESS_MODALS"] = "1"
    return $map
}

function Start-SandboxedProc([string]$ExePath, [string[]]$ArgsList, [string]$WorkDir = "", [switch]$Isolated, $CustomEnv = $null) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new($ExePath)
    $psi.Arguments = (($ArgsList | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '(\\*)"', '$1$1\"') + '"' } else { $_ } }) -join " ")
    if ($WorkDir) { $psi.WorkingDirectory = $WorkDir }
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $false
    $psi.EnvironmentVariables.Clear()
    $envMap = if ($null -ne $CustomEnv) { $CustomEnv } else { Build-SandboxEnv }
    foreach ($kv in $envMap.GetEnumerator()) { $psi.EnvironmentVariables[$kv.Key] = $kv.Value }
    if ($Isolated) { $psi.EnvironmentVariables["APP_ISOLATED_TEST"] = "1" }
    return [System.Diagnostics.Process]::Start($psi)
}

function Get-FreePort { $l = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([System.Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); return $p }
function Invoke-Api([int]$Port, [string]$Tok, [string]$Method, [string]$Route, $Body = $null) {
    $h = @{ Authorization = "Bearer $Tok" }; $u = "http://127.0.0.1:$Port$Route"
    if ($null -eq $Body) { return Invoke-RestMethod -Uri $u -Method $Method -Headers $h -TimeoutSec 10 }
    return Invoke-RestMethod -Uri $u -Method $Method -Headers $h -ContentType "application/json" -Body ($Body | ConvertTo-Json -Compress) -TimeoutSec 10
}
function Find-Node($Node, [string]$Id) {
    if ($null -eq $Node) { return $null }; if ($Node.id -eq $Id) { return $Node }
    foreach ($c in @($Node.children)) { $m = Find-Node $c $Id; if ($null -ne $m) { return $m } }
    return $null
}
function Assert-NonBlankPng([string]$Path, [int]$MinColors = 3, [int]$MinNonBlack = 4) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Screenshot missing: $Path" }
    $b = [System.IO.File]::ReadAllBytes($Path); if ($b.Length -lt 4096 -or $b[0] -ne 0x89 -or $b[1] -ne 0x50) { throw "Invalid PNG: $Path" }
    $bmp = [System.Drawing.Bitmap]::new($Path)
    try {
        if ($bmp.Width -lt 400 -or $bmp.Height -lt 300) { throw "PNG too small: $($bmp.Width)x$($bmp.Height)" }
        $cols = [System.Collections.Generic.HashSet[int]]::new(); $nonBlack = 0
        for ($y = 0; $y -lt 32; $y++) { for ($x = 0; $x -lt 32; $x++) {
            $c = $bmp.GetPixel([int](($x + 0.5) * $bmp.Width / 32.0), [int](($y + 0.5) * $bmp.Height / 32.0))
            [void]$cols.Add($c.ToArgb()); if ($c.R -gt 8 -or $c.G -gt 8 -or $c.B -gt 8) { $nonBlack++ }
        } }
        if ($cols.Count -lt $MinColors -or $nonBlack -lt $MinNonBlack) {
            throw "Screenshot '$Path' failed diversity check (colors=$($cols.Count)/$MinColors, nonBlack=$nonBlack/$MinNonBlack)."
        }
    } finally { $bmp.Dispose() }
}

function Wait-ExeboxStreamReady([string]$StreamLogPath, [int]$WaitMs = 8000) {
    $deadline = (Get-Date).AddMilliseconds($WaitMs)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $StreamLogPath) {
            try {
                $txt = [System.IO.File]::ReadAllText($StreamLogPath)
                if ($txt -match 'Started ffmpeg') { Start-Sleep -Milliseconds 800; return }
            } catch { }
        }
        Start-Sleep -Milliseconds 150
    }
    Start-Sleep -Milliseconds 500
}

function Resolve-PlayerChildPid([int]$ParentPid, [int]$WaitMs = 4000) {
    $deadline = (Get-Date).AddMilliseconds($WaitMs)
    while ((Get-Date) -lt $deadline) {
        try {
            $child = Get-CimInstance Win32_Process -Filter "ParentProcessId = $ParentPid" -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -ilike "reCart*.exe" } | Select-Object -First 1
            if ($child -and [int]$child.ProcessId -gt 0) { return [int]$child.ProcessId }
        } catch { }
        Start-Sleep -Milliseconds 150
    }
    return $ParentPid
}

function Invoke-WizardAutomation([string]$ExePath, [string[]]$ExtraArgs, [string]$ShotPath, [string]$ClickProfileId) {
    $port = Get-FreePort; $tok = [Guid]::NewGuid().ToString("N"); $streamProc = $null; $proc = $null
    $sbSnapDir = Join-Path $sandbox "snaps"; New-Item -ItemType Directory -Force $sbSnapDir | Out-Null
    $sbShot = Join-Path $sbSnapDir (Split-Path -Leaf $ShotPath)
    $doneLeaf = [System.IO.Path]::GetFileNameWithoutExtension($ShotPath) + "_complete.png"
    $sbDoneShot = Join-Path $sbSnapDir $doneLeaf
    $doneOutPath = Join-Path (Split-Path -Parent $ShotPath) $doneLeaf
    $streamLog = Join-Path $sbLogs ("stream_wiz_" + $port + ".log")
    try {
        $proc = Start-SandboxedProc $ExePath ($ExtraArgs + @("--automation-port", "$port", "--automation-token", $tok, "--no-launch"))
        $ok = $false
        for ($i = 0; $i -lt 80; $i++) { if ($proc.HasExited) { throw "Wizard exited prematurely ($($proc.ExitCode))" }; try { if ((Invoke-Api $port $tok "GET" "/v1/health").isPassed) { $ok = $true; break } } catch {}; Start-Sleep -Milliseconds 200 }
        if (-not $ok) { throw "Wizard health timeout on port $port" }
        $streamProc = Start-ExeboxLiveVlcStream -AutomationPort $port -AutomationToken $tok -TargetPid $proc.Id -SnapshotDir (Join-Path $sandbox "stream_tmp") -LogPath $streamLog -UdpPort 5004 -Fps 20
        Wait-ExeboxStreamReady $streamLog
        if ($ClickProfileId) { $null = Invoke-Api $port $tok "POST" "/v1/pointer" @{ action = "click"; targetId = $ClickProfileId }; Start-Sleep -Milliseconds 600 }
        $null = Invoke-Api $port $tok "POST" "/v1/pointer" @{ action = "click"; targetId = "Wizard_DetailsToggle" }; Start-Sleep -Milliseconds 800
        $null = Invoke-Api $port $tok "POST" "/v1/snapshot" @{ filePath = $sbShot }
        Assert-NonBlankPng $sbShot
        Copy-Item -LiteralPath $sbShot -Destination $ShotPath -Force
        $null = Invoke-Api $port $tok "POST" "/v1/pointer" @{ action = "click"; targetId = "Wizard_PrimaryButton" }
        $done = $false; $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            if ($proc.HasExited) { throw "Wizard exited during install ($($proc.ExitCode))" }
            $b = Find-Node (Invoke-Api $port $tok "GET" "/v1/tree").data "Wizard_PrimaryButton"
            if ($b -and $b.isEnabled -and ($b.text -in @("Finish", "Launch", "Close"))) { $done = $true; break }
            Start-Sleep -Milliseconds 250
        }
        if (-not $done) { throw "Wizard install timed out for $ExePath" }
        Start-Sleep -Milliseconds 900
        $null = Invoke-Api $port $tok "POST" "/v1/snapshot" @{ filePath = $sbDoneShot }
        Assert-NonBlankPng $sbDoneShot
        Copy-Item -LiteralPath $sbDoneShot -Destination $doneOutPath -Force
        try { $null = Invoke-Api $port $tok "POST" "/v1/pointer" @{ action = "click"; targetId = "Wizard_PrimaryButton" } } catch {}
        if (-not $proc.WaitForExit(15000) -or $proc.ExitCode -ne 0) { throw "Wizard exit failed ($($proc.ExitCode))" }
    } finally {
        Stop-ExeboxLiveVlcStream $streamProc
        if ($proc -and -not $proc.HasExited) { $proc.Kill(); $null = $proc.WaitForExit(3000) }
    }
}

function Compress-Gz([string]$Src, [string]$Dst) {
    $inFs = [System.IO.File]::OpenRead($Src); try { $outFs = [System.IO.File]::Create($Dst); try { $gz = [System.IO.Compression.GZipStream]::new($outFs, [System.IO.Compression.CompressionLevel]::Fastest); try { $inFs.CopyTo($gz) } finally { $gz.Dispose() } } finally { $outFs.Dispose() } } finally { $inFs.Dispose() }
}

Remove-Item Env:APP_ISOLATED_TEST -ErrorAction SilentlyContinue
$buildsDir = Join-Path $root "Builds\v$Version"; $launcherRel = Join-Path $buildsDir "$codedSlug.exe"
$relDir = Join-Path $root "Releases\v$Version"
$onlineRel = Join-Path $relDir "$codedSlug-Setup-v$Version-win-x64.exe"
$offlineRel = Join-Path $relDir "$codedSlug-Offline-Setup-v$Version-win-x64.exe"
$portableRel = Join-Path $relDir "$codedSlug-v$Version-win-x64-Portable.zip"

try {
    if (-not $SkipReleaseGate) {
        Write-Host "[S2-01] Probing Release --automation-port ExitCode=2 gate on Launcher + Online Setup + Offline Setup..."
        $pL = Start-SandboxedProc $launcherRel @("--automation-port", "5199", "--data-dir", (Join-Path $sbD "rel_p\data"), "--log-dir", (Join-Path $sbD "rel_p\logs")) -Isolated
        $pOn = Start-SandboxedProc $onlineRel @("--automation-port", "5199") -Isolated
        $pOff = Start-SandboxedProc $offlineRel @("--automation-port", "5199") -Isolated
        foreach ($pair in @(@{ N = "Launcher"; P = $pL }, @{ N = "OnlineSetup"; P = $pOn }, @{ N = "OfflineSetup"; P = $pOff })) {
            if (-not $pair.P.WaitForExit(15000) -or $pair.P.ExitCode -ne 2) { throw "Release gate failed on $($pair.N): expected ExitCode=2, got $($pair.P.ExitCode)" }
        }
    }

    if (-not $SkipUiAutomation) {
        Write-Host "[S2-02] Publishing IronFrameTestAutomation=true Wizard binaries (-t:Rebuild,Publish) with Offline Full runtimes..."
        $autoOut = Join-Path $sandbox "auto_bins"; $instProj = Join-Path $devDir "src\IronFrameCart01.Installer\IronFrameCart01.Installer.csproj"
        $payloadZip = Join-Path $sandbox "payload.zip"; [System.IO.Compression.ZipFile]::CreateFromDirectory($buildsDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Fastest, $false)
        $reEngSrc = Join-Path $workspaceProjects "SharedLibs\reEngine\v0.6.2\reEngine-Setup-v0.6.2-win-x64.exe"
        $reCartSrc = Join-Path $workspacePersonal "reCartOne\Releases\v0.1.3\reCart-Setup-v0.1.3-win-x64.exe"
        $offProps = @("-p:InstallerPayloadZip=$payloadZip")
        if (Test-Path -LiteralPath $reEngSrc) { $reGz = Join-Path $sandbox "reEngine.gz"; Compress-Gz $reEngSrc $reGz; $offProps += "-p:ReEngineBootstrapGz=$reGz" }
        if (Test-Path -LiteralPath $reCartSrc) { $rcGz = Join-Path $sandbox "reCart.gz"; Compress-Gz $reCartSrc $rcGz; $offProps += "-p:ReCartBootstrapGz=$rcGz" }
        $baseMf = Join-Path $devDir "Installer\installer.manifest.json"; $onMf = Join-Path $sandbox "on.manifest.json"; $offMf = Join-Path $sandbox "off.manifest.json"
        $pBytes = (Get-Item -LiteralPath $portableRel).Length; $pSha = (Get-FileHash -LiteralPath $portableRel -Algorithm SHA256).Hash.ToLowerInvariant()
        $obj = (Get-Content -LiteralPath $baseMf -Raw) | ConvertFrom-Json
        $obj.version = $Version; $obj.installerDeliveryKind = "onlineSetup"; $obj.payloadAssets = @([ordered]@{ installProfile = "*"; fileName = (Split-Path -Leaf $portableRel); expectedSizeBytes = $pBytes; expectedSha256 = $pSha })
        [System.IO.File]::WriteAllText($onMf, ($obj | ConvertTo-Json -Depth 6))
        $objOff = (Get-Content -LiteralPath $baseMf -Raw) | ConvertFrom-Json; $objOff.version = $Version; $objOff.installerDeliveryKind = "offlineSetup"
        $objOff.PSObject.Properties.Remove("payloadDownloadUrl"); $objOff.PSObject.Properties.Remove("payloadAssets")
        [System.IO.File]::WriteAllText($offMf, ($objOff | ConvertTo-Json -Depth 6))

        & dotnet publish $instProj -c Release -r win-x64 --self-contained false "-t:Rebuild,Publish" -p:IronFrameTestAutomation=true -p:Version="$Version" `
            -p:InstallerAssemblyName="$codedSlug-Offline-Setup-v$Version-win-x64" -p:InstallerManifestPath="$offMf" @offProps -o (Join-Path $autoOut "off") /warnaserror --disable-build-servers -p:UseSharedCompilation=false | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Automation Offline Setup publish failed" }
        & dotnet publish $instProj -c Release -r win-x64 --self-contained false "-t:Rebuild,Publish" -p:IronFrameTestAutomation=true -p:Version="$Version" `
            -p:InstallerAssemblyName="$codedSlug-Setup-v$Version-win-x64" -p:InstallerManifestPath="$onMf" -o (Join-Path $autoOut "on") /warnaserror --disable-build-servers -p:UseSharedCompilation=false | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Automation Online Setup publish failed" }

        Write-Host "[S2-03] Automating Offline Setup (Full profile) + Online Setup (--offline-bundle) Wizards via exebox-stream..."
        $offWiz = Join-Path $autoOut "off\$codedSlug-Offline-Setup-v$Version-win-x64.exe"
        $onWiz = Join-Path $autoOut "on\$codedSlug-Setup-v$Version-win-x64.exe"
        $sbBundle = Join-Path $sandbox (Split-Path -Leaf $portableRel); Copy-Item -LiteralPath $portableRel -Destination $sbBundle -Force
        Invoke-WizardAutomation $offWiz @("--install-dir", $sbOffGame, "--suite-dir", $sbSuite, "--log-dir", (Join-Path $sbLogs "wiz_off")) (Join-Path $SnapshotDir "stage2_offline_wizard.png") "Wizard_ProfileFull"
        if (-not (Test-Path -LiteralPath (Join-Path $sbSuite "reEngine.exe")) -or (Test-Path -LiteralPath (Join-Path $sbSuite "reEngine\reEngine.exe"))) {
            throw "Offline Full Wizard failed reEngine layout check (double nesting or missing reEngine.exe)."
        }
        if (-not (Test-Path -LiteralPath (Join-Path $sbSuite "engines\reCart\v0.1.3\app\reCart.Player.exe"))) {
            throw "Offline Full Wizard missing reCart under engines\reCart\v0.1.3."
        }
        Invoke-WizardAutomation $onWiz @("--offline-bundle", $sbBundle, "--install-dir", $sbOnGame, "--suite-dir", $sbSuite, "--log-dir", (Join-Path $sbLogs "wiz_on")) (Join-Path $SnapshotDir "stage2_online_wizard.png") "Wizard_ProfileLightweight"
    }

    if (-not $SkipInstallerSandbox) {
        Write-Host "[S2-03b] Verifying Release Online adjacent Portable discovery + Release Offline Full clean default coded install..."
        $adjDir = Join-Path $sandbox "online_adj"; New-Item -ItemType Directory -Force $adjDir | Out-Null
        $adjOnlineExe = Join-Path $adjDir (Split-Path -Leaf $onlineRel)
        Copy-Item -LiteralPath $onlineRel -Destination $adjOnlineExe -Force
        Copy-Item -LiteralPath $portableRel -Destination (Join-Path $adjDir (Split-Path -Leaf $portableRel)) -Force
        $pAdj = Start-SandboxedProc $adjOnlineExe @("--silent", "--no-launch", "--no-shortcuts", "--install-dir", $sbAdjGame, "--suite-dir", $sbSuite, "--log-dir", (Join-Path $sbLogs "adj"))
        if (-not $pAdj.WaitForExit($TimeoutSeconds * 1000) -or $pAdj.ExitCode -ne 0) { throw "Online adjacent Portable discovery failed ($($pAdj.ExitCode))" }
        if (-not (Test-Path -LiteralPath (Join-Path $sbAdjGame "$codedSlug.exe"))) { throw "Adjacent Online install missing $codedSlug.exe" }

        $defSb = Join-Path $sandbox "def_full_box"; $defC = Join-Path $defSb "C"; $defD = Join-Path $defSb "D"
        $defProf = Join-Path $defC "Users\Sandbox"; $defLocal = Join-Path $defProf "AppData\Local"; $defTmp = Join-Path $defLocal "Temp"
        New-Item -ItemType Directory -Force $defC, $defD, $defProf, $defLocal, $defTmp, (Join-Path $defSb "registry"), (Join-Path $defSb "locks") | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $defSb "registry\registry.hive.json"), '{"schemaVersion":1,"registryHives":{"HKCU":{},"HKLM":{},"HKCR":{}}}')
        $defEnv = Build-SandboxEnv $defSb $defC $defD $defProf $defLocal $defTmp
        $pDef = Start-SandboxedProc $offlineRel @("--silent", "--no-launch", "--no-shortcuts", "--profile", "full", "--log-dir", (Join-Path $defSb "logs")) -CustomEnv $defEnv
        if (-not $pDef.WaitForExit($TimeoutSeconds * 1000) -or $pDef.ExitCode -ne 0) { throw "Default Offline Full install failed ($($pDef.ExitCode))" }
        $defSuite = Join-Path $defLocal "Programs\StreamItAll\reEngine"
        $defGame = Join-Path $defSuite "games\$codedSlug"
        foreach ($reqFile in @((Join-Path $defSuite "reEngine.exe"), (Join-Path $defSuite "engines\reCart\v0.1.3\app\reCart.Player.exe"), (Join-Path $defGame "$codedSlug.exe"), (Join-Path $defGame ".ready"))) {
            if (-not (Test-Path -LiteralPath $reqFile)) { throw "Clean default Offline Full missing expected artifact: $reqFile" }
        }
        if (Test-Path -LiteralPath (Join-Path $defSuite "reEngine\reEngine.exe")) { throw "Default Offline Full still created nested reEngine\reEngine!" }
        if (@(Get-ChildItem -LiteralPath $defSb -Recurse -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -ilike "*MetalGear*" }).Count -gt 0) {
            throw "Non-coded legacy name detected inside clean default install!"
        }

        Write-Host "[S2-04] Verifying silent upgrade preserving data\rom\ and data\config\settings*.json..."
        $romSentinel = Join-Path $sbOffGame "data\rom\upgrade_keep.nes"; $cfgDir = Join-Path $sbOffGame "data\config"
        New-Item -ItemType Directory -Force (Split-Path -Parent $romSentinel), $cfgDir | Out-Null
        $romBytes = [byte[]](0x4E, 0x45, 0x53, 0x1A, 0x08, 0x00, 0x20, 0x00, 0xAA, 0xBB)
        [System.IO.File]::WriteAllBytes($romSentinel, $romBytes)
        [System.IO.File]::WriteAllText((Join-Path $cfgDir "settings.video.json"), '{"schemaVersion":2,"appId":"reengine","domain":"video","resolutionScale":4,"backendId":"opengl"}')
        $pUpg = Start-SandboxedProc $offlineRel @("--silent", "--install-dir", $sbOffGame, "--suite-dir", $sbSuite, "--profile", "lightweight", "--no-launch", "--log-dir", (Join-Path $sbLogs "upg"))
        if (-not $pUpg.WaitForExit($TimeoutSeconds * 1000) -or $pUpg.ExitCode -ne 0) { throw "Silent upgrade failed ($($pUpg.ExitCode))" }
        if (-not (Test-Path -LiteralPath $romSentinel) -or ([System.BitConverter]::ToString([System.IO.File]::ReadAllBytes($romSentinel)) -ne [System.BitConverter]::ToString($romBytes))) { throw "data\rom\ was not preserved across upgrade!" }
        $vidJson = Get-Content -LiteralPath (Join-Path $cfgDir "settings.video.json") -Raw | ConvertFrom-Json
        if ($vidJson.resolutionScale -ne 4) { throw "data\config\settings.video.json was not preserved across upgrade!" }
        $coreJson = Get-Content -LiteralPath (Join-Path $cfgDir "settings.json") -Raw | ConvertFrom-Json
        if ($coreJson.installProfile -ne "lightweight") { throw "settings.json missing persisted installProfile!" }
        Remove-Item -LiteralPath $romSentinel -Force -ErrorAction SilentlyContinue
    }

    Write-Host "[S2-05] Verifying Launcher --dry-run / --verify & optional ephemeral BYO AOT + 20,000-step lockstep..."
    $cartCandidates = @((Join-Path $sbSuite "engines\reCart\v0.1.3"), (Join-Path $sbSuite "engines\reCart\0.1.3"))
    if (-not [string]::IsNullOrWhiteSpace($env:RECART_BUILD_DIR)) { $cartCandidates += $env:RECART_BUILD_DIR }
    $cartCandidates += @((Join-Path $workspacePersonal "reCartOne\Builds\v0.1.3"), (Join-Path $workspacePersonal "reCartOne\Builds\Latest"))
    $cartBuild = $cartCandidates | Where-Object { Test-Path -LiteralPath (Join-Path $_ "app\reCart.Player.exe") } | Select-Object -First 1
    if (-not $cartBuild) { throw "Sibling reCart.Player.exe build not found for Launcher verification." }
    $launchExe = if (Test-Path -LiteralPath (Join-Path $sbOffGame "$codedSlug.exe")) { Join-Path $sbOffGame "$codedSlug.exe" } else { $launcherRel }
    $lData = Join-Path $sbD "launch_test\data"; $lLogs = Join-Path $sbD "launch_test\logs"
    New-Item -ItemType Directory -Force $lData, $lLogs | Out-Null
    foreach ($flag in @("--dry-run", "--verify")) {
        $pCmd = Start-SandboxedProc $launchExe @($flag, "--reengine-dir", $cartBuild, "--data-dir", $lData, "--log-dir", $lLogs) (Split-Path -Parent $launchExe)
        if (-not $pCmd.WaitForExit(30000) -or $pCmd.ExitCode -ne 0) { throw "Launcher $flag failed ($($pCmd.ExitCode))" }
    }

    $byoOutcome = "SKIP"
    if (-not [string]::IsNullOrWhiteSpace($ByoRomPath) -and (Test-Path -LiteralPath $ByoRomPath)) {
        $ephemRoot = Join-Path $sbTemp ("mgr_byo_" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
        $ephemRomDir = Join-Path $ephemRoot "data\rom"; $ephemRom = Join-Path $ephemRomDir "NES-ME-USA.nes"
        New-Item -ItemType Directory -Force $ephemRomDir, (Join-Path $ephemRoot "logs") | Out-Null
        try {
            Copy-Item -LiteralPath $ByoRomPath -Destination $ephemRom -Force
            $pLock = Start-SandboxedProc $launchExe @("--rom", $ephemRom, "--reengine-dir", $cartBuild, "--data-dir", (Join-Path $ephemRoot "data"), "--log-dir", (Join-Path $ephemRoot "logs"), "--lockstep-oracle", "--lockstep-steps", "20000", "--wait") (Split-Path -Parent $launchExe)
            if (-not $pLock.WaitForExit($TimeoutSeconds * 1000) -or $pLock.ExitCode -ne 0) { throw "BYO 20,000-step lockstep failed ($($pLock.ExitCode))" }
            $aotDlls = @(Get-ChildItem -LiteralPath (Join-Path $ephemRoot "data\cache") -Filter *.Generated.dll -Recurse -ErrorAction SilentlyContinue)
            if ($aotDlls.Count -lt 1) { throw "Local AOT Generated.dll missing under data\cache\" }
            $byoOutcome = "PASS ($($aotDlls[0].Name))"
            Write-Host "[S2-05] Ephemeral BYO AOT + 20k lockstep PASSED ($($aotDlls[0].Name))."
        } finally { Remove-Item -LiteralPath $ephemRoot -Recurse -Force -ErrorAction SilentlyContinue }
    } else {
        Write-Host "[S2-05] SKIP: Optional BYO ROM absent (CI zero-asset pass)."
    }

    if (-not $SkipUiAutomation) {
        Write-Host "[S2-06..S2-09] Publishing Automation Harness & running deep 7-Point prelaunch sandbox E2E..."
        $autoGameDir = Join-Path $sbGames $codedSlug
        $autoCartApp = Join-Path $sbSuite "engines\reCart\v0.1.3\app"
        $autoLauncherExe = Publish-Stage2AutomationHarness -Version $Version -CodedSlug $codedSlug -DevDir $devDir -BuildsDir $buildsDir -WorkspacePersonal $workspacePersonal -AutoGameDir $autoGameDir -AutoCartApp $autoCartApp

        Invoke-Stage2MissingAndCorruptMediaProbes -AutoLauncherExe $autoLauncherExe -AutoGameDir $autoGameDir -SbSuite $sbSuite -SbGames $sbGames -Sandbox $sandbox -SbLogs $sbLogs -SnapshotDir $SnapshotDir

        if (-not [string]::IsNullOrWhiteSpace($ByoRomPath) -and (Test-Path -LiteralPath $ByoRomPath)) {
            $liveSummary = Invoke-Stage2FormatAndLiveGameplayProbes -AutoLauncherExe $autoLauncherExe -AutoGameDir $autoGameDir -AutoCartApp $autoCartApp -SbSuite $sbSuite -Sandbox $sandbox -SbLogs $sbLogs -SnapshotDir $SnapshotDir -ByoRomPath $ByoRomPath
            $byoOutcome = "$byoOutcome + $liveSummary"
            Invoke-Stage2StandaloneRawRomGuardProbe -AutoCartApp $autoCartApp -ByoRomPath $ByoRomPath -Sandbox $sandbox -SbLogs $sbLogs -SnapshotDir $SnapshotDir
        } else {
            Write-Host "[S2-07..09] SKIP: Optional BYO ROM absent (CI zero-asset pass skipped)."
        }
    }

    $e2eSummary = "[$(([DateTime]::UtcNow).ToString('yyyy-MM-ddTHH:mm:ss.fffZ'))] STAGE 2 PASSED v$Version Desktop='$($preflight.desktopName)' ViewerActive=$($preflight.viewerActive) ByoLockstep='$byoOutcome'"
    [System.IO.File]::WriteAllText((Join-Path $logsDir "stage2_e2e_v$Version.log"), ($e2eSummary + "`r`n"), [System.Text.UTF8Encoding]::new($false))
    Write-Host "STAGE 2 SANDBOX E2E PASSED (Snapshots in '$SnapshotDir')."
} finally {
    if (-not $SandboxRoot -and (Test-Path -LiteralPath $sandbox)) { Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue }
    if (-not $SandboxRoot -and (Test-Path -LiteralPath $sbBase) -and @(Get-ChildItem -LiteralPath $sbBase -Force -ErrorAction SilentlyContinue).Count -eq 0) { Remove-Item -LiteralPath $sbBase -Force -ErrorAction SilentlyContinue }
}
