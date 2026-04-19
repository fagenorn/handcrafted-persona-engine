<#
.SYNOPSIS
    Updates install-manifest.json with SHA256 + size for each HuggingFace-sourced asset
    by hashing the corresponding file under assets-source/.
.DESCRIPTION
    Reads the manifest, walks every asset whose Source is HuggingFace, locates the
    file at assets-source/<source.path>, recomputes Sha256 + SizeBytes, and writes
    the updated manifest back.

    NVIDIA assets are skipped silently (their hashes come from NVIDIA's
    redistrib_<version>.json and live in the manifest as "FROM_NVIDIA_MANIFEST").
.PARAMETER WhatIf
    Preview the changes that would be written without modifying the manifest.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $ManifestPath = (Join-Path $PSScriptRoot '..\src\PersonaEngine\PersonaEngine.Lib\Assets\Manifest\install-manifest.json'),
    [string] $AssetsRoot   = (Join-Path $PSScriptRoot '..\assets-source')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw "Manifest not found: $ManifestPath"
}
if (-not (Test-Path -LiteralPath $AssetsRoot)) {
    throw "Assets root not found: $AssetsRoot"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -Depth 32

$hashed     = 0
$updated    = 0
$missing    = @()
$totalBytes = [int64]0

foreach ($asset in $manifest.assets) {
    if ($asset.source.type -ne 'HuggingFace') { continue }

    $local = Join-Path $AssetsRoot $asset.source.path
    if (-not (Test-Path -LiteralPath $local)) {
        $missing += [PSCustomObject]@{ Id = $asset.id; Expected = $local }
        continue
    }

    $hash = (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $local).Length
    $hashed++
    $totalBytes += $size

    if ($asset.sha256 -ne $hash -or $asset.sizeBytes -ne $size) {
        if ($PSCmdlet.ShouldProcess("$($asset.id) ($local)", "update sha256 + sizeBytes")) {
            $asset.sha256    = $hash
            $asset.sizeBytes = $size
        }
        $updated++
    }
}

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "Missing local files for $($missing.Count) HuggingFace asset(s):" -ForegroundColor Yellow
    foreach ($m in $missing) {
        Write-Host "  - $($m.Id) -> $($m.Expected)" -ForegroundColor Yellow
    }
    throw "Refusing to write manifest while $($missing.Count) HuggingFace asset(s) are missing under $AssetsRoot."
}

if ($PSCmdlet.ShouldProcess($ManifestPath, "write updated manifest")) {
    $json = $manifest | ConvertTo-Json -Depth 32
    [System.IO.File]::WriteAllText($ManifestPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
}

$mb = [Math]::Round($totalBytes / 1MB, 2)
Write-Host ""
Write-Host "Hashed   : $hashed asset(s) ($mb MB total)" -ForegroundColor Green
Write-Host "Updated  : $updated asset(s) in $ManifestPath" -ForegroundColor Green
if ($WhatIfPreference) {
    Write-Host "(WhatIf) No file was written." -ForegroundColor Cyan
}
