# assets-source/

Local-only staging for assets uploaded to
https://huggingface.co/fagenorn/persona-engine-assets and consumed at install
time by `BootstrapRunner` via `install-manifest.json`.

## Why this directory is gitignored

The files here are large binary models (Whisper/Kokoro/Qwen3/RVC/Audio2Face).
Hosting them in the git repo would balloon clone size and make the
installer pipeline slow. They live in HuggingFace Hub instead — git stores
only their content hash and version pin in `install-manifest.json`.

## Layout

The directory **mirrors the HuggingFace repo paths exactly**. Each file's
relative path under `assets-source/` must match the corresponding manifest
entry's `source.path`. For example:

```
assets-source/
├── audio2face/
│   └── audio2face.zip                        # → audio2face-bundle
├── kokoro/
│   ├── lexicons.zip                          # → kokoro-lexicons
│   ├── model_slim.onnx                       # → kokoro-model
│   ├── phoneme_to_id.txt                     # → kokoro-phoneme-mappings
│   └── voices-default.zip                    # → kokoro-voices
├── live2d/
│   └── aria.zip                              # → live2d-aria
├── mdx/
│   └── mdx.zip                               # → mdx-bundle
├── mel_band_roformer/
│   └── melbandroformer_optimized.onnx        # → mel-band-roformer
├── opennlp/
│   └── opennlp.zip                           # → opennlp-bundle
├── profanity/
│   └── profanity.zip                         # → profanity-bundle
├── qwen3-tts/
│   └── qwen3-tts.zip                         # → qwen3-tts-bundle
├── rvc/
│   ├── crepe_tiny.onnx                       # → rvc-crepe-tiny
│   ├── rmvpe.onnx                            # → rvc-rmvpe
│   ├── vec-768-layer-12.onnx                 # → rvc-hubert
│   └── voices-default.zip                    # → rvc-voices
├── silero-vad/
│   └── silero_vad_v5.onnx                    # → silero-vad
├── vision/
│   └── MARKER                                # → vision-llm-marker
├── wav2vec2/
│   └── wav2vec2.zip                          # → wav2vec2-ctc-aligner
└── whisper/
    ├── ggml-large-v3-turbo.bin               # → whisper-large-v3-turbo
    └── ggml-tiny.en.bin                      # → whisper-tiny-en
```

Note: NVIDIA runtime archives (CUDA cudart/cublas/cufft, cuDNN) are *not*
staged here — the bootstrapper fetches them directly from NVIDIA's
redistributable CDN.

## Adding a new asset

1. Drop the file (or zip) under `assets-source/` at the path you intend the
   manifest entry's `source.path` to reference.
2. Add a new entry to
   `src/PersonaEngine/PersonaEngine.Lib/Assets/Manifest/install-manifest.json`:
   - `source.path`: the relative path you just dropped the file at
   - `installPath`: where the runtime expects it under `<BaseDir>/Resources/`
     (e.g. `kokoro/model_slim.onnx`)
   - `gates`: the `FeatureIds` constants this asset enables; every gate
     string must already exist in
     `src/PersonaEngine/PersonaEngine.Lib/Assets/FeatureIds.cs`
     (otherwise `FeatureIdsCoverageTests` fails)
   - `extractArchive: true` if `source.path` is a zip
3. Refresh hashes:
   ```sh
   python scripts/hash-manifest.py
   ```
   Use `--dry-run` to preview without writing. The script refuses to write
   while any HuggingFace asset is missing under `assets-source/`.
4. Commit the updated `install-manifest.json` (this is the only thing the
   git repo records about your new asset).
5. When ready to ship the new revision:
   ```pwsh
   ./scripts/upload-hf-assets.ps1 -Revision vX.Y.Z
   ```
   This uploads `assets-source/` to the HF repo on `main`, then tags the
   resulting commit with `vX.Y.Z`. Bump every manifest entry's
   `source.revision` to the same tag — `ManifestValidationTests` enforces
   that no entry references the floating `main` branch.

## Updating an existing asset

Replace the file under `assets-source/`, re-run `hash-manifest.py`, then
re-upload with a *new* revision tag. Never overwrite an existing tag —
clients pin to specific revisions for reproducible installs.
