<#
.SYNOPSIS
    Updates install-manifest.json with SHA256 + size for each file under assets-source/.
.DESCRIPTION
    Reads the manifest, finds each asset whose Source is HuggingFace, locates the corresponding
    file under assets-source/<source.path>, recomputes Sha256 + SizeBytes, and writes back.
    NVIDIA assets are not touched (their hashes come from NVIDIA's redistrib_<version>.json).
#>
param(
    [string] $ManifestPath = (Join-Path $PSScriptRoot '..\src\PersonaEngine\PersonaEngine.Lib\Assets\Manifest\install-manifest.json'),
    [string] $AssetsRoot   = (Join-Path $PSScriptRoot '..\assets-source')
)

$ErrorActionPreference = 'Stop'

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -Depth 32
$updated  = 0

foreach ($asset in $manifest.assets) {
    if ($asset.source.type -ne 'HuggingFace') { continue }
    $local = Join-Path $AssetsRoot $asset.source.path
    if (-not (Test-Path $local)) {
        Write-Warning "Missing local file for $($asset.id): $local"
        continue
    }
    $hash = (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $local).Length
    if ($asset.sha256 -ne $hash -or $asset.sizeBytes -ne $size) {
        $asset.sha256    = $hash
        $asset.sizeBytes = $size
        $updated++
    }
}

$json = $manifest | ConvertTo-Json -Depth 32
[System.IO.File]::WriteAllText($ManifestPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Host "Updated $updated asset(s) in $ManifestPath."
