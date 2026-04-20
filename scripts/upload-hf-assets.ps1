<#
.SYNOPSIS
    Uploads assets-source/ to the configured HuggingFace repo and tags it with the given revision.
.PARAMETER Revision
    Required. Git tag to create on the HF repo (e.g. "v1.0.0"). Must match manifest entries.
.PARAMETER Repo
    HF model repo to upload into (default: fagenorn/persona-engine-assets).
.PARAMETER AssetsRoot
    Local directory mirroring the HF repo layout (default: <repo-root>/assets-source).
#>
param(
    [Parameter(Mandatory)] [string] $Revision,
    [string] $Repo       = 'fagenorn/persona-engine-assets',
    [string] $AssetsRoot = (Join-Path $PSScriptRoot '..\assets-source')
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command hf -ErrorAction SilentlyContinue)) {
    throw "hf CLI not found on PATH. Install with: pip install -U huggingface_hub"
}

if (-not (Test-Path -LiteralPath $AssetsRoot)) {
    throw "Assets root not found: $AssetsRoot"
}

Write-Host "Repo     : $Repo"
Write-Host "Revision : $Revision"
Write-Host "Source   : $AssetsRoot"
Write-Host ""

Write-Host "Uploading $AssetsRoot to $Repo (revision=main) via upload-large-folder..."
# upload-large-folder is the supported path for multi-GB uploads: parallel
# workers, per-file commits, and resumable on retry. The single-commit
# `hf upload` command silently stalls on big trees from this network.
#
# `--exclude README.md` keeps any local-only README (notes, scratch docs)
# out of the public assets repo. The bootstrapper never references README
# files in the install manifest, so excluding it is safe and prevents the
# generated landing page from appearing on the HF model card.
hf upload-large-folder $Repo $AssetsRoot --repo-type=model --revision=main --exclude "README.md"
if ($LASTEXITCODE -ne 0) { throw "Upload failed (exit $LASTEXITCODE)." }

Write-Host ""
Write-Host "Creating tag '$Revision' on $Repo..."
hf repos tag create $Repo $Revision --revision main
if ($LASTEXITCODE -ne 0) { throw "Tag failed (exit $LASTEXITCODE)." }

$browseUrl = "https://huggingface.co/$Repo/tree/$Revision"
Write-Host ""
Write-Host "Done. Manifest entries should reference revision='$Revision'." -ForegroundColor Green
Write-Host "Verify the tagged tree at: $browseUrl" -ForegroundColor Green
