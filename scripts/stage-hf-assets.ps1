<#
.SYNOPSIS
    Stages assets-source/ from the dev-tree Resources/ directory.
.DESCRIPTION
    Mirrors install-manifest.json's HuggingFace asset paths into assets-source/
    by copying single files and building zip bundles for extractArchive entries.

    Re-runnable: existing files/zips are overwritten so the result always matches
    the dev tree exactly.
.PARAMETER ResourcesRoot
    Source of truth (default: <repo-root>/src/PersonaEngine/PersonaEngine.Lib/Resources).
.PARAMETER AssetsRoot
    Output staging root (default: <repo-root>/assets-source).
#>
param(
    [string] $ResourcesRoot = (Join-Path $PSScriptRoot '..\src\PersonaEngine\PersonaEngine.Lib\Resources'),
    [string] $AssetsRoot    = (Join-Path $PSScriptRoot '..\assets-source')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ResourcesRoot)) {
    throw "ResourcesRoot not found: $ResourcesRoot"
}

# Resolve to absolute paths so subsequent joins are unambiguous.
$ResourcesRoot = (Resolve-Path -LiteralPath $ResourcesRoot).Path
if (-not (Test-Path -LiteralPath $AssetsRoot)) {
    New-Item -ItemType Directory -Path $AssetsRoot -Force | Out-Null
}
$AssetsRoot = (Resolve-Path -LiteralPath $AssetsRoot).Path

function Copy-Single {
    param([string] $RelSource, [string] $RelDest)

    $src = Join-Path $ResourcesRoot $RelSource
    $dst = Join-Path $AssetsRoot $RelDest
    if (-not (Test-Path -LiteralPath $src)) { throw "Missing source: $src" }
    $dstDir = Split-Path -Parent $dst
    if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
    Copy-Item -LiteralPath $src -Destination $dst -Force
    $size = [Math]::Round((Get-Item -LiteralPath $dst).Length / 1MB, 2)
    Write-Host ("  copy  {0,8} MB  {1}" -f $size, $RelDest)
}

function New-ZipBundle {
    param(
        [string]   $RelDest,        # e.g. 'kokoro/voices-default.zip'
        [string]   $StagingDirName, # temp dir name under $env:TEMP
        [scriptblock] $Populate     # builds the staging dir contents (flat layout)
    )

    $dst = Join-Path $AssetsRoot $RelDest
    $dstDir = Split-Path -Parent $dst
    if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "persona-stage-$StagingDirName"
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    try {
        & $Populate $tempRoot
        if (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Force }
        # Use .NET ZipFile so subdirectories are included recursively with a
        # flat layout (CreateFromDirectory takes the dir's children as zip root).
        # Compress-Archive misbehaves on some recursive trees.
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $tempRoot,
            $dst,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false   # includeBaseDirectory = false → flat layout
        )
        $size = [Math]::Round((Get-Item -LiteralPath $dst).Length / 1MB, 2)
        Write-Host ("  zip   {0,8} MB  {1}" -f $size, $RelDest)
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function New-MarkerFile {
    param([string] $RelDest)
    $dst = Join-Path $AssetsRoot $RelDest
    $dstDir = Split-Path -Parent $dst
    if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
    Set-Content -LiteralPath $dst -Value '' -NoNewline -Encoding utf8
    Write-Host ("  mark           0 MB  {0}" -f $RelDest)
}

Write-Host "Staging assets from $ResourcesRoot"
Write-Host "                 to $AssetsRoot"
Write-Host ""

# --- Single-file copies ---------------------------------------------------
Copy-Single 'silero-vad\silero_vad_v5.onnx'                'silero-vad\silero_vad_v5.onnx'
Copy-Single 'whisper\ggml-tiny.en.bin'                     'whisper\ggml-tiny.en.bin'
Copy-Single 'whisper\ggml-large-v3-turbo.bin'              'whisper\ggml-large-v3-turbo.bin'
Copy-Single 'kokoro\model_slim.onnx'                       'kokoro\model_slim.onnx'
Copy-Single 'kokoro\phoneme_to_id.txt'                     'kokoro\phoneme_to_id.txt'
Copy-Single 'rvc\vec-768-layer-12.onnx'                    'rvc\vec-768-layer-12.onnx'
Copy-Single 'rvc\crepe_tiny.onnx'                          'rvc\crepe_tiny.onnx'
Copy-Single 'rvc\rmvpe.onnx'                               'rvc\rmvpe.onnx'
Copy-Single 'mel_band_roformer\melbandroformer_optimized.onnx' 'mel_band_roformer\melbandroformer_optimized.onnx'

# --- Zip bundles (flat layout) -------------------------------------------
New-ZipBundle 'profanity\profanity.zip' 'profanity' {
    param($staging)
    Copy-Item -LiteralPath (Join-Path $ResourcesRoot 'profanity\badwords.txt')                  -Destination $staging
    Copy-Item -LiteralPath (Join-Path $ResourcesRoot 'profanity\tiny_toxic_detector.onnx')      -Destination $staging
    Copy-Item -LiteralPath (Join-Path $ResourcesRoot 'profanity\tiny_toxic_detector_vocab.txt') -Destination $staging
}

New-ZipBundle 'opennlp\opennlp.zip' 'opennlp' {
    param($staging)
    # Whole opennlp/ tree (flat .nbin files + Coref/, NameFind/, Parser/ subdirs)
    Copy-Item -Path (Join-Path $ResourcesRoot 'opennlp\*') -Destination $staging -Recurse -Force
}

New-ZipBundle 'kokoro\voices-default.zip' 'kokoro-voices' {
    param($staging)
    Copy-Item -Path (Join-Path $ResourcesRoot 'kokoro\voices\*.bin') -Destination $staging -Force
}

New-ZipBundle 'kokoro\lexicons.zip' 'kokoro-lexicons' {
    param($staging)
    Copy-Item -Path (Join-Path $ResourcesRoot 'kokoro\lexicons\*.json') -Destination $staging -Force
}

New-ZipBundle 'qwen3-tts\qwen3-tts.zip' 'qwen3-tts' {
    param($staging)
    # Whole qwen3-tts/ tree (flat top-level files + embeddings/, speakers/)
    Copy-Item -Path (Join-Path $ResourcesRoot 'qwen3-tts\*') -Destination $staging -Recurse -Force
}

New-ZipBundle 'rvc\voices-default.zip' 'rvc-voices' {
    param($staging)
    Copy-Item -Path (Join-Path $ResourcesRoot 'rvc\voices\*') -Destination $staging -Force
}

New-ZipBundle 'wav2vec2\wav2vec2.zip' 'wav2vec2' {
    param($staging)
    Copy-Item -LiteralPath (Join-Path $ResourcesRoot 'wav2vec2\vocab.json') -Destination $staging
    # Preserve the onnx/ subdir as the runtime expects ctc/onnx/model.onnx.
    $onnxDir = Join-Path $staging 'onnx'
    New-Item -ItemType Directory -Path $onnxDir -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $ResourcesRoot 'wav2vec2\onnx\model.onnx') -Destination $onnxDir
}

New-ZipBundle 'live2d\aria.zip' 'live2d-aria' {
    param($staging)
    # Whole aria/ subtree
    Copy-Item -Path (Join-Path $ResourcesRoot 'live2d\aria\*') -Destination $staging -Recurse -Force
}

New-ZipBundle 'audio2face\audio2face.zip' 'audio2face' {
    param($staging)
    Copy-Item -Path (Join-Path $ResourcesRoot 'audio2face\*') -Destination $staging -Recurse -Force
}

New-ZipBundle 'mdx\mdx.zip' 'mdx' {
    param($staging)
    Copy-Item -Path (Join-Path $ResourcesRoot 'mdx\*') -Destination $staging -Force
}

# --- Marker --------------------------------------------------------------
New-MarkerFile 'vision\MARKER'

Write-Host ""
Write-Host "Done. Run 'python scripts/hash-manifest.py --dry-run' to verify resolution." -ForegroundColor Green
