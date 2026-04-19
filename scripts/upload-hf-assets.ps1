<#
.SYNOPSIS
    Uploads assets-source/ to the configured HuggingFace repo and tags it with the given revision.
.PARAMETER Revision
    Required. Git tag to create on the HF repo (e.g. "v1.0.0"). Must match manifest entries.
#>
param(
    [Parameter(Mandatory)] [string] $Revision,
    [string] $Repo       = 'fagenorn/persona-engine-assets',
    [string] $AssetsRoot = (Join-Path $PSScriptRoot '..\assets-source')
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command huggingface-cli -ErrorAction SilentlyContinue)) {
    throw "huggingface-cli not found. Install with: pip install -U huggingface_hub"
}

Write-Host "Uploading $AssetsRoot to $Repo..."
huggingface-cli upload $Repo $AssetsRoot . --repo-type=model --revision=main
if ($LASTEXITCODE -ne 0) { throw "Upload failed." }

Write-Host "Creating tag $Revision on $Repo..."
huggingface-cli repo tag $Repo $Revision --revision main
if ($LASTEXITCODE -ne 0) { throw "Tag failed." }

Write-Host "Done. Manifest entries should reference revision='$Revision'."
