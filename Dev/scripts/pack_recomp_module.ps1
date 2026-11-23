<#
.SYNOPSIS
    Packs strictly 2 clean-room JSON entries [module.recomp.json, aot-recipe.json] into IronFrame-OuterFortress-Cart-01.recomp.
.DESCRIPTION
    Asserts 0 .dll, 0 *.g.cs, 0 *.Generated.dll, 0 .nes/.prg binaries, valid JSON/schema parity, and Zip-Slip containment.
#>
[CmdletBinding()]
param(
    [string]$ManifestPath = "",
    [string]$RecipePath = "",
    [string]$OutputPath = "",
    [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$devDir = Split-Path -Parent $scriptDir
$repoRoot = Split-Path -Parent $devDir

$defaultManifestPath = Join-Path $devDir "module.recomp.json"
$defaultRecipePath = Join-Path $devDir "src\IronFrameCart01.Module\Manifests\aot-recipe.json"
$archiveFileName = "IronFrame-OuterFortress-Cart-01.recomp"

if ([string]::IsNullOrWhiteSpace($ManifestPath)) { $ManifestPath = $defaultManifestPath }
if ([string]::IsNullOrWhiteSpace($RecipePath)) { $RecipePath = $defaultRecipePath }

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $versionFile = Join-Path $devDir "version.txt"
    $versionText = if (Test-Path -LiteralPath $versionFile) { (Get-Content -LiteralPath $versionFile -Raw -Encoding UTF8).Trim() } else { "0.1.0" }
    if ([string]::IsNullOrWhiteSpace($versionText)) { $versionText = "0.1.0" }
    $OutputPath = Join-Path $repoRoot ("Builds\v{0}\app\{1}" -f $versionText, $archiveFileName)
}
elseif ((Test-Path -LiteralPath $OutputPath -PathType Container) -or $OutputPath.EndsWith("\") -or $OutputPath.EndsWith("/")) {
    $OutputPath = Join-Path $OutputPath $archiveFileName
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)

function Assert-CleanRoomJsonPayload {
    param(
        [string]$RawText,
        [string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($RawText)) {
        throw "Clean-room validation failure: '$Label' is empty."
    }

    $leakPatterns = @(
        @{ Name = "Local User Profile"; Regex = '(?i)C:[\\/]+Users[\\/]+(?!Public|Default)[A-Za-z0-9._-]+' },
        @{ Name = "Workspace Drive Path"; Regex = '(?i)D:[\\/]+(Projects|Programs)[\\/]+' },
        @{ Name = "Commercial Trademark"; Regex = '(?i)\b(metal[\s_-]*gear|konami|nintendo)\b' }
    )
    foreach ($pat in $leakPatterns) {
        if ($RawText -match $pat.Regex) {
            throw "Clean-room R03/PII gate failed in '$Label': matched $($pat.Name)."
        }
    }
}

function Assert-ManifestAndRecipePair {
    param(
        [string]$ManifestRaw,
        [string]$RecipeRaw
    )

    $manifestObj = $ManifestRaw | ConvertFrom-Json
    $recipeObj = $RecipeRaw | ConvertFrom-Json

    if (-not $manifestObj -or [int]$manifestObj.schemaVersion -lt 1 -or [string]::IsNullOrWhiteSpace($manifestObj.moduleId)) {
        throw "Invalid module.recomp.json: missing valid schemaVersion or moduleId."
    }
    if (-not $recipeObj -or [int]$recipeObj.schemaVersion -lt 1 -or [string]::IsNullOrWhiteSpace($recipeObj.moduleId)) {
        throw "Invalid aot-recipe.json: missing valid schemaVersion or moduleId."
    }
    if ($manifestObj.moduleId -cne $recipeObj.moduleId) {
        throw "Cross-manifest mismatch: module.recomp.json moduleId='$($manifestObj.moduleId)' != aot-recipe.json moduleId='$($recipeObj.moduleId)'."
    }
    if (($manifestObj.platformId -ine "nes") -or ($recipeObj.platformId -ine "nes")) {
        throw "Platform mismatch: expected 'nes' in both manifests."
    }
    if ([int]$manifestObj.mapperId -ne [int]$recipeObj.mapperId) {
        throw "Mapper mismatch: module.recomp.json mapperId=$($manifestObj.mapperId) != aot-recipe.json mapperId=$($recipeObj.mapperId)."
    }

    $seedCount = if ($recipeObj.PSObject.Properties["seeds"] -and $null -ne $recipeObj.seeds) { @($recipeObj.seeds).Count } else { 0 }
    $entryCount = if ($recipeObj.PSObject.Properties["entryAddresses"] -and $null -ne $recipeObj.entryAddresses) { @($recipeObj.entryAddresses).Count } else { 0 }
    if (($seedCount + $entryCount) -lt 1) {
        throw "Invalid aot-recipe.json '$($recipeObj.moduleId)': must declare at least 1 seed or entryAddress."
    }
}

function Assert-CleanRoomRecompArchive {
    param([string]$ArchivePath)

    if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
        throw "Clean-room .recomp archive not found: '$ArchivePath'."
    }

    $forbiddenSuffixes = @(".generated.dll", ".g.cs", ".cs", ".dll", ".exe", ".pdb", ".xml", ".nes", ".prg", ".fds", ".chr", ".rom", ".bin", ".sav")
    $virtualRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "mgr_recomp_zip_guard"))
    $virtualRootWithSep = $virtualRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

    $zip = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entryNames = [System.Collections.Generic.List[string]]::new()
        $payloads = @{}

        foreach ($entry in $zip.Entries) {
            $rawName = $entry.FullName
            $normalized = $rawName.Replace('\', '/')
            $entryNames.Add($normalized)

            if ($normalized.EndsWith('/')) {
                throw "Zip-Slip / clean-room violation: directory entries are prohibited ('$rawName')."
            }
            $osRelative = $normalized.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            if ([System.IO.Path]::IsPathRooted($osRelative) -or $normalized.Contains(':') -or $normalized.StartsWith('/') -or $normalized.Contains('..')) {
                throw "Zip-Slip containment violation: entry '$rawName' escapes archive root."
            }

            $candidate = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($virtualRoot, $osRelative))
            if (-not $candidate.StartsWith($virtualRootWithSep, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Zip-Slip containment violation: '$rawName' resolved outside virtual root."
            }

            foreach ($suffix in $forbiddenSuffixes) {
                if ($normalized.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "Clean-room violation in '$ArchivePath': forbidden entry '$rawName' ($suffix)."
                }
            }

            $entryStream = $entry.Open()
            try {
                $ms = [System.IO.MemoryStream]::new()
                try {
                    $entryStream.CopyTo($ms)
                    $bytes = $ms.ToArray()
                    if ($bytes.Length -ge 4) {
                        $isNes = ($bytes[0] -eq 0x4E -and $bytes[1] -eq 0x45 -and $bytes[2] -eq 0x53 -and $bytes[3] -eq 0x1A)
                        $isFds = ($bytes[0] -eq 0x46 -and $bytes[1] -eq 0x44 -and $bytes[2] -eq 0x53 -and $bytes[3] -eq 0x1A)
                        $isPe = ($bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A)
                        if ($isNes -or $isFds -or $isPe) {
                            throw "Clean-room binary magic violation inside '$rawName'."
                        }
                    }
                    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
                    Assert-CleanRoomJsonPayload -RawText $text -Label "$ArchivePath::$normalized"
                    $payloads[$normalized] = $text
                } finally {
                    $ms.Dispose()
                }
            } finally {
                $entryStream.Dispose()
            }
        }

        $actualSorted = @($entryNames | Sort-Object)
        $expectedSorted = @("aot-recipe.json", "module.recomp.json")
        if ($actualSorted.Count -ne 2 -or $actualSorted[0] -cne $expectedSorted[0] -or $actualSorted[1] -cne $expectedSorted[1]) {
            throw "Clean-room .recomp archive must contain strictly 2 entries [module.recomp.json, aot-recipe.json]; found [$($entryNames -join ', ')]."
        }

        Assert-ManifestAndRecipePair -ManifestRaw $payloads["module.recomp.json"] -RecipeRaw $payloads["aot-recipe.json"]
    } finally {
        $zip.Dispose()
    }
}

if ($VerifyOnly) {
    Assert-CleanRoomRecompArchive -ArchivePath $resolvedOutput
    $verifiedBytes = (Get-Item -LiteralPath $resolvedOutput).Length
    Write-Output "Verified clean-room 2-member archive '$resolvedOutput' ($verifiedBytes B): [module.recomp.json, aot-recipe.json]; 0 .dll, 0 *.g.cs, 0 *.Generated.dll, 0 .nes/.prg, Zip-Slip OK."
    exit 0
}

$resolvedManifest = [System.IO.Path]::GetFullPath($ManifestPath)
$resolvedRecipe = [System.IO.Path]::GetFullPath($RecipePath)
if (-not (Test-Path -LiteralPath $resolvedManifest -PathType Leaf)) {
    throw "Module manifest not found: '$resolvedManifest'."
}
if (-not (Test-Path -LiteralPath $resolvedRecipe -PathType Leaf)) {
    throw "AOT recipe JSON not found: '$resolvedRecipe'."
}

$manifestJson = Get-Content -LiteralPath $resolvedManifest -Raw -Encoding UTF8
$recipeJson = Get-Content -LiteralPath $resolvedRecipe -Raw -Encoding UTF8
Assert-CleanRoomJsonPayload -RawText $manifestJson -Label $resolvedManifest
Assert-CleanRoomJsonPayload -RawText $recipeJson -Label $resolvedRecipe
Assert-ManifestAndRecipePair -ManifestRaw $manifestJson -RecipeRaw $recipeJson

$outDir = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$tempArchive = Join-Path $outDir (".tmp_" + [Guid]::NewGuid().ToString("N") + ".recomp")
try {
    $zipWrite = [System.IO.Compression.ZipFile]::Open($tempArchive, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($pair in @(
            @{ Name = "module.recomp.json"; Content = $manifestJson },
            @{ Name = "aot-recipe.json"; Content = $recipeJson }
        )) {
            $entry = $zipWrite.CreateEntry($pair.Name, [System.IO.Compression.CompressionLevel]::Optimal)
            $bytes = $utf8NoBom.GetBytes($pair.Content)
            $stream = $entry.Open()
            try {
                $stream.Write($bytes, 0, $bytes.Length)
            } finally {
                $stream.Dispose()
            }
        }
    } finally {
        $zipWrite.Dispose()
    }

    Assert-CleanRoomRecompArchive -ArchivePath $tempArchive
    Move-Item -LiteralPath $tempArchive -Destination $resolvedOutput -Force
} finally {
    if (Test-Path -LiteralPath $tempArchive) {
        Remove-Item -LiteralPath $tempArchive -Force -ErrorAction SilentlyContinue
    }
}

Assert-CleanRoomRecompArchive -ArchivePath $resolvedOutput
$finalBytes = (Get-Item -LiteralPath $resolvedOutput).Length
Write-Output "Packed clean-room 2-member archive '$resolvedOutput' ($finalBytes B): [module.recomp.json, aot-recipe.json]; 0 .dll, 0 *.g.cs, 0 *.Generated.dll, 0 .nes/.prg, Zip-Slip OK."
