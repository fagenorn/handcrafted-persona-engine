<#
.SYNOPSIS
    Publishes the App in Release config and produces PersonaEngine-<version>-win-x64.zip.
.DESCRIPTION
    Runs `dotnet publish` for PersonaEngine.App, strips the bootstrap-managed
    asset subdirectories from the publish output (those are downloaded by the
    in-app bootstrapper on first launch), zips the remainder, and asserts the
    archive is below a 1.8 GB hard ceiling (GitHub release-asset limit is 2 GB,
    we keep margin).

    Baked-in resources (Shaders, Fonts, Imgs, Prompts, native/) are preserved.
.PARAMETER Version
    Required. SemVer string like "1.0.0" or "0.0.0-dev".
.PARAMETER OutputDir
    Where the publish-<version>/ tree and the final .zip are written.
    Defaults to <repo-root>/artifacts.
.PARAMETER WhatIf
    Preview the strip + zip operations without modifying disk.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $OutputDir = (Join-Path $PSScriptRoot '..\artifacts')
)

$ErrorActionPreference = 'Stop'

# Locate dotnet: env override first (CI sets DOTNET_EXE=dotnet to use the
# system install), then the local-dev default under %USERPROFILE%\.dotnet.
$DotNet = if ($env:DOTNET_EXE) {
    $env:DOTNET_EXE
} else {
    Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
}

if (-not (Get-Command $DotNet -ErrorAction SilentlyContinue)) {
    throw "dotnet not found at '$DotNet'. Set DOTNET_EXE or install the .NET 9 SDK."
}

$Project    = Join-Path $PSScriptRoot '..\src\PersonaEngine\PersonaEngine.App\PersonaEngine.App.csproj'
$PublishDir = Join-Path $OutputDir "publish-$Version"
$ZipPath    = Join-Path $OutputDir "PersonaEngine-$Version-win-x64.zip"

if (-not (Test-Path -LiteralPath $Project)) {
    throw "App project not found: $Project"
}

Write-Host "dotnet     : $DotNet"
Write-Host "Project    : $Project"
Write-Host "Version    : $Version"
Write-Host "PublishDir : $PublishDir"
Write-Host "ZipPath    : $ZipPath"
Write-Host ""

if (Test-Path -LiteralPath $PublishDir) {
    if ($PSCmdlet.ShouldProcess($PublishDir, 'remove stale publish dir')) {
        Remove-Item -Recurse -Force -LiteralPath $PublishDir
    }
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "Publishing $Project..."
& $DotNet publish $Project -c Release -o $PublishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit $LASTEXITCODE)."
}
if (-not (Test-Path -LiteralPath $PublishDir)) {
    throw "Publish completed but $PublishDir does not exist."
}

# Bootstrap-managed subsystems - installer downloads these on first run.
# Source of truth: src/PersonaEngine/PersonaEngine.Lib/PersonaEngine.Lib.csproj
# (the <Content Include="Resources\<sub>\**\*"> blocks for ML asset subsystems)
$bootstrapManagedSubdirs = @(
    'silero-vad', 'whisper', 'profanity', 'kokoro', 'qwen3-tts',
    'wav2vec2', 'opennlp', 'rvc', 'audio2face', 'mdx',
    'mel_band_roformer', 'live2d', 'cuda', 'cudnn'
)

$resourcesDir = Join-Path $PublishDir 'Resources'

Write-Host ""
Write-Host "Stripping bootstrap-managed subdirs from $resourcesDir..."

$totalStrippedBytes = [int64]0
foreach ($sub in $bootstrapManagedSubdirs) {
    $target = Join-Path $resourcesDir $sub
    if (-not (Test-Path -LiteralPath $target)) { continue }

    $bytes = (Get-ChildItem -LiteralPath $target -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    if (-not $bytes) { $bytes = 0 }
    $mb = [Math]::Round($bytes / 1MB, 1)

    if ($PSCmdlet.ShouldProcess($target, "strip ($mb MB)")) {
        Remove-Item -Recurse -Force -LiteralPath $target
        Write-Host ("  stripped Resources/{0,-20} {1,8} MB" -f $sub, $mb)
        $totalStrippedBytes += $bytes
    } else {
        Write-Host ("  would strip Resources/{0,-15} {1,8} MB" -f $sub, $mb)
    }
}
$totalStrippedMB = [Math]::Round($totalStrippedBytes / 1MB, 1)
Write-Host "Total stripped: $totalStrippedMB MB"

Write-Host ""
Write-Host "Shipped Resources/ contents:"
if (Test-Path -LiteralPath $resourcesDir) {
    $remaining = Get-ChildItem -LiteralPath $resourcesDir -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name
    if ($remaining) {
        foreach ($dir in $remaining) {
            $bytes = (Get-ChildItem -LiteralPath $dir.FullName -Recurse -File -ErrorAction SilentlyContinue |
                Measure-Object -Property Length -Sum).Sum
            if (-not $bytes) { $bytes = 0 }
            $mb = [Math]::Round($bytes / 1MB, 1)
            Write-Host ("  Resources/{0,-20} {1,8} MB" -f $dir.Name, $mb)
        }
    } else {
        Write-Host "  (Resources/ contains no subdirectories)"
    }
} else {
    Write-Host "  (no Resources/ directory in publish output)"
}

if (Test-Path -LiteralPath $ZipPath) {
    if ($PSCmdlet.ShouldProcess($ZipPath, 'remove stale zip')) {
        Remove-Item -Force -LiteralPath $ZipPath
    }
}

Write-Host ""
Write-Host "Compressing $PublishDir -> $ZipPath..."
if ($PSCmdlet.ShouldProcess($ZipPath, 'create release zip')) {
    Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal
    if (-not (Test-Path -LiteralPath $ZipPath)) {
        throw "Compress-Archive completed but $ZipPath does not exist."
    }

    $sizeBytes = (Get-Item -LiteralPath $ZipPath).Length
    $sizeMB    = [Math]::Round($sizeBytes / 1MB, 1)
    $limitMB   = 1800
    Write-Host ""
    Write-Host "Built $ZipPath ($sizeMB MB)" -ForegroundColor Green
    if ($sizeBytes -gt $limitMB * 1MB) {
        throw "Release zip exceeds ${limitMB}MB hard ceiling (GitHub limit is 2 GB; we keep margin)."
    }
} else {
    Write-Host "(WhatIf) No zip was created." -ForegroundColor Cyan
}
