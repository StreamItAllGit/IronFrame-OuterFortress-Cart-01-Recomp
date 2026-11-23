#Requires -Version 5.1
# @module: Dev/scripts/Stage2PrelaunchProbes.ps1
# @purpose: Production-like 7-Point Stage 2 sandbox UI, format matrix (.7z/.zip/.prg/.nes), Cold AOT (<5s), F3 zero-jitter, F1 persistence, and standalone raw-ROM probes.
# @depends-on: Dev/scripts/run_stage2_sandbox_e2e.ps1, Foundation ExeboxStreamHelpers.ps1
# @exports: Publish-Stage2AutomationHarness, Invoke-Stage2MissingAndCorruptMediaProbes, Invoke-Stage2FormatAndLiveGameplayProbes, Invoke-Stage2StandaloneRawRomGuardProbe

function Wait-Stage2Health {
    param(
        [Parameter(Mandatory = $true)]$Process,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$Label,
        [double]$MaxSeconds = 5.0
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $limitMs = [int]([Math]::Max($MaxSeconds * 1000.0, 5000.0))
    while ($sw.ElapsedMilliseconds -lt $limitMs) {
        if ($Process.HasExited) { throw "$Label exited prematurely ($($Process.ExitCode))." }
        try {
            $resp = Invoke-Api $Port $Token "GET" "/v1/health"
            if ($resp.isPassed) {
                $sw.Stop()
                if ($sw.Elapsed.TotalSeconds -gt $MaxSeconds) {
                    throw ("{0} automation/window health took {1:N2}s (exceeded {2:N1}s gate)." -f $Label, $sw.Elapsed.TotalSeconds, $MaxSeconds)
                }
                return $sw.Elapsed.TotalSeconds
            }
        } catch {}
        Start-Sleep -Milliseconds 120
    }
    throw "$Label automation health timed out after ${MaxSeconds}s on port $Port."
}

function Wait-Stage2TreeNode {
    param(
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][string]$NodeId,
        [bool]$ShouldExist = $true,
        [int]$TimeoutMs = 20000,
        $Process = $null
    )
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        if ($Process -and $Process.HasExited) { throw "Process exited ($($Process.ExitCode)) while waiting for '$NodeId'." }
        try {
            $tree = (Invoke-Api $Port $Token "GET" "/v1/tree").data
            $node = Find-Node $tree $NodeId
            if ($ShouldExist -and $null -ne $node) { return $tree }
            if (-not $ShouldExist -and $null -eq $node) { return $tree }
        } catch {}
        Start-Sleep -Milliseconds 120
    }
    throw "Timed out waiting for node '$NodeId' (ShouldExist=$ShouldExist)."
}

function Open-Stage2F1Tab {
    param([int]$Port, [string]$Token, [string]$TabId)
    $tree = (Invoke-Api $Port $Token "GET" "/v1/tree").data
    if ($null -eq (Find-Node $tree "Drawer_CloseButton")) {
        for ($try = 0; $try -lt 2; $try++) {
            $null = Invoke-Api $Port $Token "POST" "/v1/keyboard" @{ action = "press"; key = "F1" }
            Start-Sleep -Milliseconds 450
            $tree = (Invoke-Api $Port $Token "GET" "/v1/tree").data
            if ($null -ne (Find-Node $tree "Drawer_CloseButton") -and $null -ne (Find-Node $tree $TabId)) { break }
        }
    }
    if ($null -eq (Find-Node $tree $TabId)) { throw "F1 Settings Drawer missing '$TabId'." }
    $null = Invoke-Api $Port $Token "POST" "/v1/pointer" @{ action = "click"; targetId = $TabId }
    Start-Sleep -Milliseconds 450
    return (Invoke-Api $Port $Token "GET" "/v1/tree").data
}

function Close-Stage2ViaF1Quit {
    param($Process, [int]$Port, [string]$Token, [string]$Label)
    $sysTree = Open-Stage2F1Tab -Port $Port -Token $Token -TabId "Tab_System"
    if ($null -eq (Find-Node $sysTree "System_QuitGame")) { throw "$Label missing System_QuitGame button." }
    $null = Invoke-Api $Port $Token "POST" "/v1/pointer" @{ action = "click"; targetId = "System_QuitGame" }
    if (-not $Process.WaitForExit(15000) -or $Process.ExitCode -ne 0) {
        throw "$Label clean quit via F1 failed (ExitCode=$($Process.ExitCode))."
    }
}

function Publish-Stage2AutomationHarness {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$CodedSlug,
        [Parameter(Mandatory = $true)][string]$DevDir,
        [Parameter(Mandatory = $true)][string]$BuildsDir,
        [Parameter(Mandatory = $true)][string]$WorkspacePersonal,
        [Parameter(Mandatory = $true)][string]$AutoGameDir,
        [Parameter(Mandatory = $true)][string]$AutoCartApp
    )
    if (-not (Test-Path -LiteralPath (Join-Path $AutoGameDir "app\$CodedSlug.recomp"))) {
        New-Item -ItemType Directory -Force (Join-Path $AutoGameDir "app") | Out-Null
        Copy-Item -LiteralPath (Join-Path $BuildsDir "*") -Destination $AutoGameDir -Recurse -Force
    }
    $autoEngineRoot = Split-Path -Parent $AutoCartApp
    New-Item -ItemType Directory -Force $AutoCartApp | Out-Null

    $launcherProj = Join-Path $DevDir "src\IronFrameCart01.Launcher\IronFrameCart01.Launcher.csproj"
    & dotnet publish $launcherProj -c Release -r win-x64 --self-contained false "-t:Rebuild,Publish" -p:IronFrameTestAutomation=true -p:PublishSingleFile=true -p:Version="$Version" -o $AutoGameDir /warnaserror --disable-build-servers -p:UseSharedCompilation=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Automation Launcher publish failed" }

    $reCartPlayerProj = Join-Path $WorkspacePersonal "reCartOne\Dev\src\reCart.Player\reCart.Player.csproj"
    if (-not (Test-Path -LiteralPath $reCartPlayerProj)) { throw "Missing sibling reCart.Player.csproj at $reCartPlayerProj" }
    & dotnet publish $reCartPlayerProj -c Release -r win-x64 --self-contained false "-t:Rebuild,Publish" -p:ReCartTestAutomation=true -p:PublishSingleFile=false -o $AutoCartApp /warnaserror --disable-build-servers -p:UseSharedCompilation=false | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Automation reCart.Player publish failed" }

    if (-not (Test-Path -LiteralPath (Join-Path $AutoCartApp "SDL3.dll"))) {
        Copy-Item -LiteralPath (Join-Path $DevDir "lib\SDL3.dll") -Destination (Join-Path $AutoCartApp "SDL3.dll") -Force
    }
    $toolsDst = Join-Path $AutoCartApp "tools"
    if (-not (Test-Path -LiteralPath (Join-Path $toolsDst "7z.exe"))) {
        $toolsSrc = Join-Path $WorkspacePersonal "reCartOne\Builds\v0.1.3\app\tools"
        if (Test-Path -LiteralPath $toolsSrc) {
            New-Item -ItemType Directory -Force $toolsDst | Out-Null
            Copy-Item -LiteralPath (Join-Path $toolsSrc "*") -Destination $toolsDst -Force
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $toolsDst "7z.exe"))) { throw "Bundled tools\7z.exe missing in $toolsDst." }
    [System.IO.File]::WriteAllText((Join-Path $autoEngineRoot ".ready"), '{"engineId":"reCart","version":"0.1.3","ready":true}')
    return (Join-Path $AutoGameDir "$CodedSlug.exe")
}

function Invoke-Stage2MissingAndCorruptMediaProbes {
    param(
        [string]$AutoLauncherExe, [string]$AutoGameDir, [string]$SbSuite, [string]$SbGames,
        [string]$Sandbox, [string]$SbLogs, [string]$SnapshotDir
    )
    foreach ($gDir in @(Get-ChildItem -LiteralPath $SbGames -Directory -ErrorAction SilentlyContinue)) {
        $rDir = Join-Path $gDir.FullName "data\rom"
        if (Test-Path -LiteralPath $rDir) { Get-ChildItem -LiteralPath $rDir -Force -File | Where-Object { $_.Name -ne ".keep" } | Remove-Item -Force -ErrorAction SilentlyContinue }
    }

    $portGate = Get-FreePort; $tokGate = [Guid]::NewGuid().ToString("N")
    $sbGateShot = Join-Path $Sandbox "snaps\stage2_missing_rom_gate.png"
    $sbDemoShot = Join-Path $Sandbox "snaps\stage2_demo_cart.png"
    New-Item -ItemType Directory -Force (Split-Path -Parent $sbGateShot) | Out-Null
    $pGate = $null; $sGate = $null
    try {
        $pGate = Start-SandboxedProc $AutoLauncherExe @("--suite-dir", $SbSuite, "--data-dir", (Join-Path $AutoGameDir "data"), "--log-dir", (Join-Path $AutoGameDir "logs"), "--automation-port", "$portGate", "--automation-token", $tokGate) $AutoGameDir
        $gateSec = Wait-Stage2Health -Process $pGate -Port $portGate -Token $tokGate -Label "Media Gate" -MaxSeconds 5.0
        $playerPid = Resolve-PlayerChildPid $pGate.Id
        $sGate = Start-ExeboxLiveVlcStream -AutomationPort $portGate -AutomationToken $tokGate -TargetPid $playerPid -SnapshotDir (Join-Path $Sandbox "stream_tmp") -LogPath (Join-Path $SbLogs "stream_gate.log") -UdpPort 5004 -Fps 20
        Wait-ExeboxStreamReady (Join-Path $SbLogs "stream_gate.log")
        $tree = Wait-Stage2TreeNode -Port $portGate -Token $tokGate -NodeId "MediaGate_BrowseButton" -ShouldExist $true -Process $pGate
        foreach ($btn in @("MediaGate_DropZone", "MediaGate_BrowseButton", "MediaGate_SelectModuleButton", "MediaGate_LaunchDemoButton")) {
            if ($null -eq (Find-Node $tree $btn)) { throw "Media Gate missing button '$btn'." }
        }
        $null = Invoke-Api $portGate $tokGate "POST" "/v1/snapshot" @{ filePath = $sbGateShot }
        Assert-NonBlankPng $sbGateShot -MinColors 4 -MinNonBlack 10
        Copy-Item -LiteralPath $sbGateShot -Destination (Join-Path $SnapshotDir "stage2_missing_rom_gate.png") -Force

        $null = Invoke-Api $portGate $tokGate "POST" "/v1/pointer" @{ action = "click"; targetId = "MediaGate_LaunchDemoButton" }
        $null = Wait-Stage2TreeNode -Port $portGate -Token $tokGate -NodeId "MediaGate_BrowseButton" -ShouldExist $false -TimeoutMs 10000 -Process $pGate
        $null = Wait-Stage2TreeNode -Port $portGate -Token $tokGate -NodeId "AotOverlay_Card" -ShouldExist $false -TimeoutMs 30000 -Process $pGate
        Start-Sleep -Milliseconds 600
        $null = Invoke-Api $portGate $tokGate "POST" "/v1/snapshot" @{ filePath = $sbDemoShot }
        Assert-NonBlankPng $sbDemoShot -MinColors 3 -MinNonBlack 8
        Copy-Item -LiteralPath $sbDemoShot -Destination (Join-Path $SnapshotDir "stage2_demo_cart.png") -Force
        Close-Stage2ViaF1Quit -Process $pGate -Port $portGate -Token $tokGate -Label "MediaGate+Demo"
        Write-Host ("[S2-06a] Missing-ROM Media Gate ({0:N2}s) + Demo Cart PASSED." -f $gateSec)
    } finally {
        Stop-ExeboxLiveVlcStream $sGate
        if ($pGate -and -not $pGate.HasExited) { $pGate.Kill(); $null = $pGate.WaitForExit(3000) }
    }

    $corrupt7z = Join-Path $Sandbox "corrupt_probe.7z"
    [System.IO.File]::WriteAllBytes($corrupt7z, [System.Text.Encoding]::ASCII.GetBytes("NOT-A-VALID-7Z-ARCHIVE"))
    $badLogDir = Join-Path $Sandbox "corrupt_logs"; New-Item -ItemType Directory -Force $badLogDir | Out-Null
    $portBad = Get-FreePort; $tokBad = [Guid]::NewGuid().ToString("N")
    $sbBadShot = Join-Path $Sandbox "snaps\stage2_corrupt_rom_gate.png"
    $pBad = $null; $sBad = $null
    try {
        $pBad = Start-SandboxedProc $AutoLauncherExe @("--rom", $corrupt7z, "--suite-dir", $SbSuite, "--data-dir", (Join-Path $AutoGameDir "data"), "--log-dir", $badLogDir, "--automation-port", "$portBad", "--automation-token", $tokBad) $AutoGameDir
        $null = Wait-Stage2Health -Process $pBad -Port $portBad -Token $tokBad -Label "Corrupt-7z Gate" -MaxSeconds 5.0
        $playerPidBad = Resolve-PlayerChildPid $pBad.Id
        $sBad = Start-ExeboxLiveVlcStream -AutomationPort $portBad -AutomationToken $tokBad -TargetPid $playerPidBad -SnapshotDir (Join-Path $Sandbox "stream_tmp") -LogPath (Join-Path $SbLogs "stream_corrupt.log") -UdpPort 5004 -Fps 20
        Wait-ExeboxStreamReady (Join-Path $SbLogs "stream_corrupt.log")
        $null = Wait-Stage2TreeNode -Port $portBad -Token $tokBad -NodeId "MediaGate_BrowseButton" -ShouldExist $true -Process $pBad
        $null = Invoke-Api $portBad $tokBad "POST" "/v1/snapshot" @{ filePath = $sbBadShot }
        Assert-NonBlankPng $sbBadShot -MinColors 4 -MinNonBlack 10
        Copy-Item -LiteralPath $sbBadShot -Destination (Join-Path $SnapshotDir "stage2_corrupt_rom_gate.png") -Force
        Close-Stage2ViaF1Quit -Process $pBad -Port $portBad -Token $tokBad -Label "Corrupt-7z Gate"
        $badLogTxt = [System.IO.File]::ReadAllText((Join-Path $badLogDir "recart.player.log"))
        if ($badLogTxt -notmatch 'Media preload rejected') { throw "Corrupt .7z error was not logged in recart.player.log." }
        Write-Host "[S2-06b] Corrupt .7z negative error gate PASSED."
    } finally {
        Stop-ExeboxLiveVlcStream $sBad
        if ($pBad -and -not $pBad.HasExited) { $pBad.Kill(); $null = $pBad.WaitForExit(3000) }
    }
}

function Invoke-Stage2FormatAndLiveGameplayProbes {
    param(
        [string]$AutoLauncherExe, [string]$AutoGameDir, [string]$AutoCartApp, [string]$SbSuite,
        [string]$Sandbox, [string]$SbLogs, [string]$SnapshotDir, [string]$ByoRomPath
    )
    $sevenZipExe = Join-Path $AutoCartApp "tools\7z.exe"
    $fmtWork = Join-Path $Sandbox "fmt_media"; New-Item -ItemType Directory -Force $fmtWork | Out-Null
    $nesCopy = Join-Path $fmtWork "NES-ME-USA.nes"; $prgCopy = Join-Path $fmtWork "NES-ME-USA.prg"
    $zipCopy = Join-Path $fmtWork "NES-ME-USA.zip"; $sevenZCopy = Join-Path $fmtWork "NES-ME-USA.7z"
    $rawBytes = [System.IO.File]::ReadAllBytes($ByoRomPath)
    [System.IO.File]::WriteAllBytes($nesCopy, $rawBytes)
    $prgBytes = New-Object byte[] ($rawBytes.Length - 16)
    [System.Array]::Copy($rawBytes, 16, $prgBytes, 0, $prgBytes.Length)
    [System.IO.File]::WriteAllBytes($prgCopy, $prgBytes)
    $zFs = [System.IO.File]::Create($zipCopy); try { $za = [System.IO.Compression.ZipArchive]::new($zFs, [System.IO.Compression.ZipArchiveMode]::Create); try { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $nesCopy, "NES-ME-USA.nes") | Out-Null } finally { $za.Dispose() } } finally { $zFs.Dispose() }
    & $sevenZipExe "a" "-t7z" "-y" $sevenZCopy $nesCopy | Out-Null; if ($LASTEXITCODE -ne 0) { throw "Failed to pack BYO test .7z archive ($LASTEXITCODE)." }

    $liveData = Join-Path $AutoGameDir "data"; $liveRomDir = Join-Path $liveData "rom"; $liveSaveDir = Join-Path $liveData "saves"; $liveCfgDir = Join-Path $liveData "config"; $liveLogs = Join-Path $AutoGameDir "logs"
    New-Item -ItemType Directory -Force $liveRomDir | Out-Null
    foreach ($wipeDir in @((Join-Path $liveData "cache"), $liveCfgDir, $liveSaveDir, $liveLogs)) {
        if (Test-Path -LiteralPath $wipeDir) { Get-ChildItem -LiteralPath $wipeDir -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue }
    }
    Get-ChildItem -LiteralPath $liveRomDir -Force -File | Remove-Item -Force -ErrorAction SilentlyContinue
    $live7z = Join-Path $liveRomDir "NES-ME-USA.7z"; Copy-Item -LiteralPath $sevenZCopy -Destination $live7z -Force
    $wavOut = Join-Path $Sandbox "stage2_aot_audio.wav"
    $portGame = Get-FreePort; $tokGame = [Guid]::NewGuid().ToString("N")
    $aotShot = Join-Path $Sandbox "snaps\stage2_cold_aot_overlay.png"; $bootShot = Join-Path $Sandbox "snaps\stage2_aot_boot.png"
    $playShot = Join-Path $Sandbox "snaps\stage2_aot_gameplay.png"; $hudShot = Join-Path $Sandbox "snaps\stage2_f3_hud.png"
    $f1Shot = Join-Path $Sandbox "snaps\stage2_aot_f1_drawer.png"; $relaunchShot = Join-Path $Sandbox "snaps\stage2_settings_relaunch.png"
    $pGame = $null; $sGame = $null; $coldStartSec = 0.0
    try {
        $pGame = Start-SandboxedProc $AutoLauncherExe @("--suite-dir", $SbSuite, "--data-dir", $liveData, "--log-dir", $liveLogs, "--dump-audio", $wavOut, "--automation-port", "$portGame", "--automation-token", $tokGame) $AutoGameDir
        $coldStartSec = Wait-Stage2Health -Process $pGame -Port $portGame -Token $tokGame -Label "Cold .7z Boot" -MaxSeconds 5.0
        $playerPid = Resolve-PlayerChildPid $pGame.Id
        $sGame = Start-ExeboxLiveVlcStream -AutomationPort $portGame -AutomationToken $tokGame -TargetPid $playerPid -SnapshotDir (Join-Path $Sandbox "stream_tmp") -LogPath (Join-Path $SbLogs "stream_game.log") -UdpPort 5004 -Fps 20
        $aotTree = Wait-Stage2TreeNode -Port $portGame -Token $tokGame -NodeId "AotOverlay_Card" -ShouldExist $true -TimeoutMs 6000 -Process $pGame
        if ($null -eq (Find-Node $aotTree "AotOverlay_Progress")) { throw "Cold AOT overlay missing AotOverlay_Progress bar." }
        Start-Sleep -Milliseconds 350
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/snapshot" @{ filePath = $aotShot }
        Assert-NonBlankPng $aotShot -MinColors 3 -MinNonBlack 10
        Copy-Item -LiteralPath $aotShot -Destination (Join-Path $SnapshotDir "stage2_cold_aot_overlay.png") -Force
        Wait-ExeboxStreamReady (Join-Path $SbLogs "stream_game.log")
        $postAotTree = Wait-Stage2TreeNode -Port $portGame -Token $tokGame -NodeId "AotOverlay_Card" -ShouldExist $false -TimeoutMs 90000 -Process $pGame
        if ($null -ne (Find-Node $postAotTree "MediaGate_BrowseButton")) { throw "Media Gate blocked launch when NES-ME-USA.7z was present!" }
        Start-Sleep -Milliseconds 1200
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/snapshot" @{ filePath = $bootShot }
        Assert-NonBlankPng $bootShot -MinColors 4 -MinNonBlack 10
        Copy-Item -LiteralPath $bootShot -Destination (Join-Path $SnapshotDir "stage2_aot_boot.png") -Force

        $null = Invoke-Api $portGame $tokGame "POST" "/v1/keyboard" @{ action = "press"; key = "RETURN" }; Start-Sleep -Milliseconds 1400
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/keyboard" @{ action = "press"; key = "RETURN" }; Start-Sleep -Milliseconds 1400
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/keyboard" @{ action = "press"; key = "Z" }; Start-Sleep -Milliseconds 800
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/keyboard" @{ action = "press"; key = "RIGHT" }; Start-Sleep -Milliseconds 900
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/keyboard" @{ action = "press"; key = "DOWN" }; Start-Sleep -Milliseconds 900
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/snapshot" @{ filePath = $playShot }
        Assert-NonBlankPng $playShot -MinColors 4 -MinNonBlack 10
        Copy-Item -LiteralPath $playShot -Destination (Join-Path $SnapshotDir "stage2_aot_gameplay.png") -Force
        if ((Get-FileHash $bootShot -Algorithm SHA256).Hash -eq (Get-FileHash $playShot -Algorithm SHA256).Hash) {
            throw "Gameplay frame remained static after RETURN press+release + directional inputs!"
        }

        $null = Invoke-Api $portGame $tokGame "POST" "/v1/keyboard" @{ action = "press"; key = "F3" }; Start-Sleep -Milliseconds 350
        $hudWidths = New-Object System.Collections.Generic.List[int]
        for ($sIdx = 0; $sIdx -lt 8; $sIdx++) {
            $dirKey = if ($sIdx % 2 -eq 0) { "RIGHT" } else { "LEFT" }
            $null = Invoke-Api $portGame $tokGame "POST" "/v1/keyboard" @{ action = "press"; key = $dirKey }
            Start-Sleep -Milliseconds 250
            $hNode = Find-Node (Invoke-Api $portGame $tokGame "GET" "/v1/tree").data "F3Hud"
            if ($null -eq $hNode) { throw "F3Hud node missing at sample $sIdx." }
            $hudWidths.Add([int]$hNode.width)
        }
        if (@($hudWidths | Select-Object -Unique).Count -ne 1) {
            throw "F3Hud width jittered across samples: $($hudWidths -join ', ')"
        }
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/snapshot" @{ filePath = $hudShot }
        Assert-NonBlankPng $hudShot -MinColors 4 -MinNonBlack 10
        Copy-Item -LiteralPath $hudShot -Destination (Join-Path $SnapshotDir "stage2_f3_hud.png") -Force
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/keyboard" @{ action = "press"; key = "F3" }; Start-Sleep -Milliseconds 250

        $null = Open-Stage2F1Tab -Port $portGame -Token $tokGame -TabId "Tab_Video"
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/pointer" @{ action = "click"; targetId = "Video_Scale_4x" }; Start-Sleep -Milliseconds 250
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/pointer" @{ action = "click"; targetId = "Video_Toggle_Vsync" }; Start-Sleep -Milliseconds 250
        $null = Open-Stage2F1Tab -Port $portGame -Token $tokGame -TabId "Tab_Audio"
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/pointer" @{ action = "click"; targetId = "Audio_MasterVolume_Dec" }; Start-Sleep -Milliseconds 200
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/pointer" @{ action = "click"; targetId = "Audio_MasterVolume_Dec" }; Start-Sleep -Milliseconds 300
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/snapshot" @{ filePath = $f1Shot }
        Assert-NonBlankPng $f1Shot -MinColors 6 -MinNonBlack 12
        Copy-Item -LiteralPath $f1Shot -Destination (Join-Path $SnapshotDir "stage2_aot_f1_drawer.png") -Force

        $null = Open-Stage2F1Tab -Port $portGame -Token $tokGame -TabId "Tab_System"
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/pointer" @{ action = "click"; targetId = "System_QuickSave" }; Start-Sleep -Milliseconds 700
        $null = Invoke-Api $portGame $tokGame "POST" "/v1/pointer" @{ action = "click"; targetId = "System_QuickLoad" }; Start-Sleep -Milliseconds 700
        Close-Stage2ViaF1Quit -Process $pGame -Port $portGame -Token $tokGame -Label "Live .7zGameplay"
    } finally {
        Stop-ExeboxLiveVlcStream $sGame
        if ($pGame -and -not $pGame.HasExited) { $pGame.Kill(); $null = $pGame.WaitForExit(3000) }
    }

    $saveFiles = @(Get-ChildItem -LiteralPath $liveSaveDir -Filter "*.slot0.savstate" -ErrorAction SilentlyContinue)
    if ($saveFiles.Count -lt 1 -or $saveFiles[0].Length -lt 64) { throw "QuickSave missing in '$liveSaveDir'." }
    if (-not (Test-Path -LiteralPath $wavOut) -or (Get-Item -LiteralPath $wavOut).Length -le 44) { throw "Audio WAV missing at '$wavOut'." }
    if (-not ("Stage2WavProbe" -as [type])) {
        Add-Type -TypeDefinition 'public static class Stage2WavProbe { public static int PeakInt16(byte[] b) { int pk = 0; for (int i = 44; i + 1 < b.Length; i += 2) { short s = (short)(b[i] | (b[i + 1] << 8)); int a = s == short.MinValue ? 32767 : (s < 0 ? -s : s); if (a > pk) pk = a; } return pk; } }'
    }
    $peakPcm = [Stage2WavProbe]::PeakInt16([System.IO.File]::ReadAllBytes($wavOut))
    if ($peakPcm -le 20) { throw "Audio capture was silent (peak=$peakPcm)." }
    $logTxt = [System.IO.File]::ReadAllText((Join-Path $liveLogs "recart.player.log"))
    if ($logTxt -match '\[ERROR\]|UnliftedPcTrapException') { throw "Runtime error or UnliftedPcTrapException in recart.player.log." }
    if ($logTxt -notmatch 'tier=StaticAotOnly' -or $logTxt -notmatch 'hasRom=True' -or $logTxt -notmatch '\.Generated\.dll') {
        throw "Expected StaticAotOnly + Generated.dll log evidence missing."
    }

    $vidJson = (Get-Content -LiteralPath (Join-Path $liveCfgDir "settings.video.json") -Raw) | ConvertFrom-Json
    $audJson = (Get-Content -LiteralPath (Join-Path $liveCfgDir "settings.audio.json") -Raw) | ConvertFrom-Json
    if ($vidJson.resolutionScale -ne 4 -or -not $vidJson.isVsyncEnabled) { throw "settings.video.json did not persist Scale=4 + VSync=true." }
    if ([double]$audJson.masterVolume -ge 0.75) { throw "settings.audio.json did not persist lowered masterVolume ($($audJson.masterVolume))." }

    $portRe = Get-FreePort; $tokRe = [Guid]::NewGuid().ToString("N"); $pRe = $null; $sRe = $null
    try {
        $pRe = Start-SandboxedProc $AutoLauncherExe @("--suite-dir", $SbSuite, "--data-dir", $liveData, "--log-dir", $liveLogs, "--automation-port", "$portRe", "--automation-token", $tokRe) $AutoGameDir
        $null = Wait-Stage2Health -Process $pRe -Port $portRe -Token $tokRe -Label "Settings Relaunch" -MaxSeconds 5.0
        $playerPidRe = Resolve-PlayerChildPid $pRe.Id
        $sRe = Start-ExeboxLiveVlcStream -AutomationPort $portRe -AutomationToken $tokRe -TargetPid $playerPidRe -SnapshotDir (Join-Path $Sandbox "stream_tmp") -LogPath (Join-Path $SbLogs "stream_relaunch.log") -UdpPort 5004 -Fps 20
        Wait-ExeboxStreamReady (Join-Path $SbLogs "stream_relaunch.log")
        $null = Wait-Stage2TreeNode -Port $portRe -Token $tokRe -NodeId "AotOverlay_Card" -ShouldExist $false -TimeoutMs 15000 -Process $pRe
        $null = Invoke-Api $portRe $tokRe "POST" "/v1/keyboard" @{ action = "press"; key = "F3" }; Start-Sleep -Milliseconds 300
        $audTreeRe = Open-Stage2F1Tab -Port $portRe -Token $tokRe -TabId "Tab_Audio"
        $volNode = Find-Node $audTreeRe "Audio_MasterVolume"
        $expectedPct = [int][Math]::Round([double]$audJson.masterVolume * 100.0)
        if ($null -eq $volNode -or ($volNode.text -notmatch "${expectedPct}%")) { throw "Relaunched F1 Audio_MasterVolume did not reflect persisted ${expectedPct}% ($($volNode.text))." }
        $null = Invoke-Api $portRe $tokRe "POST" "/v1/snapshot" @{ filePath = $relaunchShot }
        Assert-NonBlankPng $relaunchShot -MinColors 6 -MinNonBlack 12
        Copy-Item -LiteralPath $relaunchShot -Destination (Join-Path $SnapshotDir "stage2_settings_relaunch.png") -Force
        Close-Stage2ViaF1Quit -Process $pRe -Port $portRe -Token $tokRe -Label "Settings Relaunch"
        $vidJsonRe = (Get-Content -LiteralPath (Join-Path $liveCfgDir "settings.video.json") -Raw) | ConvertFrom-Json
        if ($vidJsonRe.resolutionScale -ne 4 -or -not $vidJsonRe.isVsyncEnabled) { throw "Relaunched settings.video.json lost Scale=4 + VSync=true." }
    } finally {
        Stop-ExeboxLiveVlcStream $sRe
        if ($pRe -and -not $pRe.HasExited) { $pRe.Kill(); $null = $pRe.WaitForExit(3000) }
    }

    foreach ($ext in @("zip", "prg")) {
        Get-ChildItem -LiteralPath $liveRomDir -Force -File | Remove-Item -Force -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath $liveLogs -Force -File | Remove-Item -Force -ErrorAction SilentlyContinue
        Copy-Item -LiteralPath (Join-Path $fmtWork "NES-ME-USA.$ext") -Destination (Join-Path $liveRomDir "NES-ME-USA.$ext") -Force
        $pFmt = Start-SandboxedProc $AutoLauncherExe @("--suite-dir", $SbSuite, "--data-dir", $liveData, "--log-dir", $liveLogs, "--headless", "--frames", "4") $AutoGameDir
        if (-not $pFmt.WaitForExit(45000) -or $pFmt.ExitCode -ne 0) { throw "Format probe .$ext failed ($($pFmt.ExitCode))." }
        $fmtLog = [System.IO.File]::ReadAllText((Join-Path $liveLogs "recart.player.log"))
        if ($fmtLog -notmatch 'hasRom=True' -or $fmtLog -match '\[ERROR\]') { throw "Format probe .$ext failed log verification." }
    }
    Get-ChildItem -LiteralPath $liveRomDir -Force -File | Remove-Item -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $wavOut -Force -ErrorAction SilentlyContinue
    Write-Host ("[S2-07/08] Cold .7z Boot ({0:N2}s), F3 0px Jitter (w={1}px), F1 Persistence & Formats (.7z/.zip/.prg/.nes) PASSED (peakPcm={2})." -f $coldStartSec, $hudWidths[0], $peakPcm)
    return "Live7z({0:N2}s, peakPcm={1}, f3w={2}px)" -f $coldStartSec, $peakPcm, $hudWidths[0]
}

function Invoke-Stage2StandaloneRawRomGuardProbe {
    param([string]$AutoCartApp, [string]$ByoRomPath, [string]$Sandbox, [string]$SbLogs, [string]$SnapshotDir)
    $stRoot = Join-Path $Sandbox "standalone_raw_rom"; $stData = Join-Path $stRoot "data"; $stLogs = Join-Path $stRoot "logs"
    New-Item -ItemType Directory -Force $stData, $stLogs | Out-Null
    $stNes = Join-Path $stRoot "standalone_probe.nes"; Copy-Item -LiteralPath $ByoRomPath -Destination $stNes -Force
    $playerExe = Join-Path $AutoCartApp "reCart.Player.exe"
    $portSt = Get-FreePort; $tokSt = [Guid]::NewGuid().ToString("N")
    $stShot = Join-Path $Sandbox "snaps\stage2_standalone_raw_rom_guard.png"
    $pSt = $null; $sSt = $null
    try {
        $pSt = Start-SandboxedProc $playerExe @("--rom", $stNes, "--data-dir", $stData, "--log-dir", $stLogs, "--automation-port", "$portSt", "--automation-token", $tokSt) $AutoCartApp
        $stSec = Wait-Stage2Health -Process $pSt -Port $portSt -Token $tokSt -Label "Standalone Raw-ROM" -MaxSeconds 5.0
        $sSt = Start-ExeboxLiveVlcStream -AutomationPort $portSt -AutomationToken $tokSt -TargetPid $pSt.Id -SnapshotDir (Join-Path $Sandbox "stream_tmp") -LogPath (Join-Path $SbLogs "stream_standalone.log") -UdpPort 5004 -Fps 20
        Wait-ExeboxStreamReady (Join-Path $SbLogs "stream_standalone.log")
        $null = Wait-Stage2TreeNode -Port $portSt -Token $tokSt -NodeId "MediaGate_SelectModuleButton" -ShouldExist $true -Process $pSt
        $null = Invoke-Api $portSt $tokSt "POST" "/v1/snapshot" @{ filePath = $stShot }
        Assert-NonBlankPng $stShot -MinColors 4 -MinNonBlack 10
        Copy-Item -LiteralPath $stShot -Destination (Join-Path $SnapshotDir "stage2_standalone_raw_rom_guard.png") -Force
        Close-Stage2ViaF1Quit -Process $pSt -Port $portSt -Token $tokSt -Label "Standalone Raw-ROM"
        $stLogTxt = [System.IO.File]::ReadAllText((Join-Path $stLogs "recart.player.log"))
        if ($stLogTxt -notmatch 'Raw-ROM launch blocked until \.recomp selected') { throw "Standalone raw-ROM guard warning missing from log." }
        Write-Host ("[S2-09] Standalone reCart Raw-ROM Clean-Room Guard ({0:N2}s) PASSED." -f $stSec)
    } finally {
        Stop-ExeboxLiveVlcStream $sSt
        if ($pSt -and -not $pSt.HasExited) { $pSt.Kill(); $null = $pSt.WaitForExit(3000) }
    }
}
