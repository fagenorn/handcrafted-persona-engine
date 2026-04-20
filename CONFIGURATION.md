<div align="center">
<h1>🔧 Configuration Reference</h1>
<img src="assets/mascot_cog.png" width="150" alt="Mascot with Cog">
<p>Every <code>appsettings.json</code> field, annotated.</p>
</div>

> [!NOTE]
> For install + first-run instructions, see [INSTALLATION.md](./INSTALLATION.md).
> Most settings below are **also editable live from the control panel** — you
> only need to touch `appsettings.json` directly for fields that don't have a
> UI surface yet, or for automated deployment. `appsettings.json` is hot-reloaded,
> so edits take effect without restarting the app.

---

## 📜 Contents

- [Where the file lives](#where)
- [Window](#window)
- [Llm](#llm)
- [Asr](#asr)
- [Microphone](#microphone)
- [Tts](#tts)
  - [Tts.Kokoro](#tts-kokoro)
  - [Tts.Qwen3](#tts-qwen3)
  - [Tts.Rvc](#tts-rvc)
  - [Tts.AuditionSample](#tts-audition)
- [LipSync](#lipsync)
- [Subtitle](#subtitle)
- [Live2D](#live2d)
- [SpoutConfigs](#spout)
- [Overlay](#overlay)
- [Vision](#vision)
- [RouletteWheel](#roulette)
- [Conversation](#conversation)
- [ConversationContext](#convcontext)

---

## <a id="where"></a>📁 Where the file lives

Next to `PersonaEngine.exe` in your install directory. On first run the release ships a sanitized template with blank API keys — fill those in (via the **LLM Connection** panel or by editing the file) and save.

From source the template is at `src/PersonaEngine/PersonaEngine.App/appsettings.template.json`; copy it to `appsettings.json` in the same folder before running.

The JSON structure is:

```jsonc
{
  "Config": {
    "Window": { ... },
    "Llm": { ... },
    ...
  }
}
```

Everything below is documented relative to `Config.*`.

> [!TIP]
> Back up `appsettings.json` before significant edits. Ensure valid JSON
> (commas, quotes, braces, brackets) — a malformed file means the app won't
> start and the log will point to the parse error.

---

## <a id="window"></a>🪟 Window

Startup size of the main control-panel window. Has no effect on the avatar or Spout output — those have their own dimensions.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Width` | int | `1366` | Initial window width in pixels. |
| `Height` | int | `768` | Initial window height in pixels. |
| `MinWidth` | int | `800` | Minimum allowed width when the user resizes. |
| `MinHeight` | int | `600` | Minimum allowed height when the user resizes. |
| `Title` | string | `"Persona Engine"` | Window title-bar text. |
| `Fullscreen` | bool | `false` | Start fullscreen. |

---

## <a id="llm"></a>🧠 Llm

LLM connection settings. **Required.** Any OpenAI-compatible endpoint works (Groq, OpenAI, Ollama via `/v1`, LM Studio, LiteLLM proxy, …). Also live-editable via the **LLM Connection** panel with a **Probe** button that verifies the endpoint answers and the model exists.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `TextApiKey` | string | `""` | API key for the text LLM. Leave blank for local endpoints that don't require auth. |
| `TextModel` | string | `"llama-3.1-8b-instant"` | Model identifier the endpoint expects (e.g. `gpt-4o-mini`, `llama3`, `your-ollama-model`). |
| `TextEndpoint` | string | `"https://api.groq.com/openai/v1"` | Base URL for the chat-completions endpoint. Must end in `/v1` or equivalent. |
| `VisionApiKey` | string | `""` | API key for the vision (screen-awareness) LLM. |
| `VisionModel` | string | `"qwen2.5-vl-3b-instruct"` | Vision-capable model identifier. |
| `VisionEndpoint` | string | `""` | Base URL for the vision LLM. Can be the same or different from the text endpoint. |
| `VisionEnabled` | bool | `false` | Master switch for the screen-awareness feature. |

---

## <a id="asr"></a>👂 Asr

Automatic speech recognition (Whisper). Live-editable from the **Listening** panel.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `TtsMode` | int | `0` | Whisper decoder preset. `0` = Performant (fast), `1` = Balanced, `2` = Precise (most accurate — needs the Turbo model from the Build-with-it profile). Exposed in the UI as the **Fast / Balanced / Accurate** chips. |
| `TtsPrompt` | string | `"Aria, Joobel"` | Optional initial prompt fed to Whisper to bias recognition toward specific names or terms. |
| `VadThreshold` | float | `0.5` | Silero VAD speech-probability threshold. Higher = stricter (requires a louder signal to be considered speech). Range `0.0–1.0`. |
| `VadThresholdGap` | float | `0.15` | Negative threshold (`VadThreshold − VadThresholdGap`). Speech detection continues while the probability stays above the negative threshold, avoiding mid-sentence cutoffs. |
| `VadMinSpeechDuration` | int (ms) | `150` | Minimum duration of audio above threshold that counts as a speech segment. Shorter bursts are ignored as noise. |
| `VadMinSilenceDuration` | int (ms) | `450` | Minimum silence required to mark the end of a segment. Shorter pauses don't end the utterance. |

---

## <a id="microphone"></a>🎤 Microphone

| Field | Type | Default | Meaning |
|---|---|---|---|
| `DeviceName` | string | `""` | Exact device name. Empty string = Windows default input. Picker available in the **Listening** panel. |

---

## <a id="tts"></a>🔊 Tts

Top-level TTS configuration. The two shippable engines (`kokoro` and `qwen3`) each have their own sub-section.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `ActiveEngine` | string | `"kokoro"` | Which engine synthesises speech. `"kokoro"` = Clear (default, phoneme-accurate, light on GPU); `"qwen3"` = Expressive (emotion / intonation, heavier). Toggled via the **Voice → Clear / Expressive** selector. |
| `EspeakPath` | string | `"espeak-ng"` | Path or command to the espeak-ng binary used as a phonemization fallback for unknown words. `espeak-ng` (just the name) works because the release bundles espeak-ng on `%PATH%`. |

### <a id="tts-kokoro"></a>Tts.Kokoro

| Field | Type | Default | Meaning |
|---|---|---|---|
| `DefaultVoice` | string | `"af_heart"` | Voice pack loaded at startup. Swap live in the **Voice** gallery. |
| `UseBritishEnglish` | bool | `false` | Bias espeak-ng fallback toward British English pronunciation rules. |
| `DefaultSpeed` | float | `1.0` | Speech rate multiplier. `1.0` = normal; `<1` slower, `>1` faster. |
| `MaxPhonemeLength` | int | `510` | Internal phoneme-buffer size per synthesis pass. Raise only if very long sentences truncate. |
| `SampleRate` | int (Hz) | `24000` | Output sample rate. Kokoro was trained at 24 kHz; changing this usually makes things worse. |
| `TrimSilence` | bool | `false` | Strip leading/trailing silence from synthesised clips. |

### <a id="tts-qwen3"></a>Tts.Qwen3

Only used when `ActiveEngine = "qwen3"`. These control the autoregressive sampler that gives Qwen3 its expressive delivery.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Speaker` | string | `"kasumiva"` | Bundled voice preset. |
| `Language` | string | `"english"` | Language tag passed to the model. |
| `Instruct` | string? | `null` | Optional instruction that nudges delivery (e.g. *"whispered"*, *"excited"*). `null` = no instruction. |
| `Temperature` | float | `0.9` | Sampling temperature. Higher = more variation in prosody; lower = more consistent. |
| `TopK` | int | `50` | Top-K sampling cutoff. |
| `TopP` | float | `1.0` | Nucleus sampling cutoff. |
| `RepetitionPenalty` | float | `1.05` | Discourages repeating the same acoustic tokens. |
| `MaxNewTokens` | int | `512` | Safety cap on tokens generated per sentence. |
| `EmitEveryFrames` | int | `8` | How many decoder frames to batch before emitting audio — smaller = lower latency, more overhead. |
| `CodePredictorGreedy` | bool | `false` | Use greedy decoding for the code predictor instead of sampled. |
| `SilencePenaltyEnabled` | bool | `true` | Penalise silence tokens to avoid awkward pauses. |

### <a id="tts-rvc"></a>Tts.Rvc

Real-time voice cloning applied **after** the TTS engine. Requires an RVC model in ONNX format; `.pth` files must be converted first.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `DefaultVoice` | string | `"KasumiVA"` | Filename (without `.onnx`) of the RVC model in `Resources/Models/rvc/voice/`. The Stream-with-it profile ships a starter pack. |
| `Enabled` | bool | `false` | Master switch. Also toggled from the Voice panel's clone layer. |
| `HopSize` | int | `64` | RVC processing window. Smaller = finer-grained timing at higher CPU/GPU cost. |
| `SpeakerId` | int | `0` | Target speaker id inside the model (if the model contains multiple). |
| `F0UpKey` | int (semitones) | `1` | Pitch shift applied by RVC. |

### <a id="tts-audition"></a>Tts.AuditionSample

Sample text used by the voice gallery's "listen to this voice" button so you can audition a voice without sending a real prompt.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Kokoro` | string | *"Hello there — I'm your new companion."* | Text sampled when auditioning Kokoro voices. |
| `Qwen3` | string | *(same)* | Text sampled when auditioning Qwen3 voices. |

---

## <a id="lipsync"></a>👄 LipSync

Lip-sync engine selection. Live-editable from **Avatar → Lip-Sync**.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Engine` | string | `"VBridger"` | `"VBridger"` = Simple (phoneme-based, always available); `"Audio2Face"` = Realistic (ML-based, requires Audio2Face assets from the Build-with-it profile). |

### LipSync.Audio2Face

Only applied when `Engine = "Audio2Face"` **and** the Audio2Face feature is installed.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Identity` | string | `"James"` | Internal Audio2Face identity preset. Not exposed in the UI by design — stick with the default unless you know what you're doing. |
| `UseGpu` | bool | `true` | Run the face solver on GPU (recommended). Falls back to CPU when `false`. |
| `SolverType` | string | `"Bvls"` | Solver backend. Leave at `Bvls` unless advised otherwise. |

---

## <a id="subtitle"></a>📜 Subtitle

Subtitle rendering. The canvas is sized to match your Spout output; the output resolution is unrelated to your monitor. Live-editable in the **Subtitles** panel.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Font` | string | `"DynaPuff.ttf"` | Filename in `Resources/Fonts/`. |
| `FontSize` | int | `125` | Point size at the canvas resolution. |
| `Color` | string (ARGB hex) | `"#FFf8f6f7"` | Fill color. |
| `HighlightColor` | string (ARGB hex) | `"#FFc4251e"` | Color used to highlight the word currently being spoken. |
| `BottomMargin` | int (px) | `250` | Distance from the canvas's bottom edge. |
| `SideMargin` | int (px) | `30` | Distance from the left/right edges. |
| `InterSegmentSpacing` | int (px) | `10` | Gap between lines. |
| `MaxVisibleLines` | int | `2` | How many lines stay on screen at once. |
| `AnimationDuration` | float (sec) | `0.3` | Fade in/out duration. |
| `StrokeThickness` | int (px) | `3` | Outline thickness (improves readability). |
| `Width` | int (px) | `1080` | Canvas width. Match your Spout output. |
| `Height` | int (px) | `1920` | Canvas height. Match your Spout output. |

---

## <a id="live2d"></a>🎭 Live2D

| Field | Type | Default | Meaning |
|---|---|---|---|
| `ModelPath` | string | `"Resources/live2d"` | Root directory containing your Live2D model folders. |
| `ModelName` | string | `"aria"` | Folder name inside `ModelPath` to load. To use a custom model, drop it into that directory and change this name. See the [Live2D Integration Guide](./Live2D.md) for rigging requirements (VBridger lip-sync parameters are mandatory). |
| `Width` | int (px) | `1080` | Render-target width. |
| `Height` | int (px) | `1920` | Render-target height. |

---

## <a id="spout"></a>📺 SpoutConfigs

Array of Spout sender configurations. Each entry becomes a selectable source inside OBS (or any other Spout receiver).

| Field | Type | Default | Meaning |
|---|---|---|---|
| `OutputName` | string | `"Live2D"` / `"RouletteWheel"` | Sender name shown in Spout receivers. |
| `Width` | int (px) | `1080` / `1080` | Output resolution width. |
| `Height` | int (px) | `1920` / `1080` | Output resolution height. |
| `Enabled` | bool | `true` | Start this sender on launch. Disable to save GPU when you're not streaming. |

Add more entries to create additional senders. The avatar and the roulette wheel are the two built-ins; other render components can declare their own Spout targets.

---

## <a id="overlay"></a>🖥️ Overlay

Transparent always-on-top desktop window that mirrors the avatar + subtitles. Toggled from the **Overlay** panel. `X / Y / Width / Height` are persisted when you drag or resize the overlay; you usually don't need to edit them by hand.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | bool | `false` | Show the overlay. |
| `Source` | string | `"Live2D"` | Which Spout source the overlay mirrors (match a `SpoutConfigs` entry). |
| `X` | int (px) | `100` | Top-left X of the overlay window on your desktop. |
| `Y` | int (px) | `100` | Top-left Y. |
| `Width` | int (px) | `360` | Overlay window width. |
| `Height` | int (px) | `640` | Overlay window height. |
| `MinWidth` | int (px) | `120` | Minimum width when dragging to resize. |
| `MinHeight` | int (px) | `120` | Minimum height when dragging to resize. |

---

## <a id="vision"></a>👀 Vision

Screen-awareness capture settings. Requires `Llm.VisionEnabled = true` and a configured `Llm.VisionEndpoint`.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `WindowTitle` | string | `"Microsoft Edge"` | Title substring of the window to capture. |
| `CaptureInterval` | TimeSpan | `"00:00:59"` | How often to snapshot (`HH:MM:SS`). |
| `CaptureMinPixels` | int | `50176` | Minimum window area (width × height). Windows smaller than this are skipped. |
| `CaptureMaxPixels` | int | `4194304` | Maximum window area. Larger windows are downscaled before being sent to the vision LLM. |

---

## <a id="roulette"></a>🎡 RouletteWheel

Optional on-screen spinning wheel. Live-editable in the **Roulette Wheel** panel.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `Font` | string | `"DynaPuff.ttf"` | Font file in `Resources/Fonts/`. |
| `FontSize` | int | `24` | Base font size for section labels. |
| `TextColor` | string (hex) | `"#FFFFFF"` | Label color. |
| `TextScale` | float | `1.0` | Extra multiplier on top of `FontSize`. |
| `TextStroke` | float (px) | `2.0` | Label outline thickness. |
| `AdaptiveText` | bool | `true` | Shrink labels automatically so long strings fit inside narrow sections. |
| `RadialTextOrientation` | bool | `true` | `true` = text radiates outward from center; `false` = horizontal text. |
| `SectionLabels` | string[] | `["Yes", "No"]` | One entry per slice. |
| `SpinDuration` | float (sec) | `8.0` | Length of the spin animation. |
| `MinRotations` | float | `5.0` | Minimum full rotations before settling. |
| `WheelSizePercentage` | float | `1.0` | Wheel size relative to its render target (0–1). |
| `Width` | int (px) | `1080` | Render-target width. Match your Spout output. |
| `Height` | int (px) | `1080` | Render-target height. |
| `PositionMode` | string | `"Anchored"` | `"Anchored"` (anchor + offset) or `"Absolute"` (raw pixel coords). |
| `ViewportAnchor` | string | `"Center"` | Anchor point when `PositionMode = "Anchored"` — one of `TopLeft`, `Top`, `TopRight`, `Left`, `Center`, `Right`, `BottomLeft`, `Bottom`, `BottomRight`. |
| `PositionXPercentage` | float | `0.5` | Horizontal position within the anchor (0–1). |
| `PositionYPercentage` | float | `0.5` | Vertical position within the anchor (0–1). |
| `AnchorOffsetX` | int (px) | `0` | Horizontal offset from the anchor. |
| `AnchorOffsetY` | int (px) | `0` | Vertical offset. |
| `AbsolutePositionX` | int (px) | `0` | X when `PositionMode = "Absolute"`. |
| `AbsolutePositionY` | int (px) | `0` | Y when `PositionMode = "Absolute"`. |
| `Enabled` | bool | `false` | Master switch. |
| `RotationDegrees` | float | `-90.0` | Initial rotation of the wheel. |
| `AnimateToggle` | bool | `true` | Animate the show/hide transition. |
| `AnimationDuration` | float (sec) | `0.5` | Show/hide animation length. |

---

## <a id="conversation"></a>💬 Conversation

Turn-level behavior.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `BargeInType` | int | `3` | How user speech interrupts the AI. `0` = Ignore (no interruption), `1` = Allow (always interruptible), `2` = NoSpeaking (only when AI isn't speaking), `3` = MinWords (need ≥ `BargeInMinWords` user words to interrupt), `4` = MinWordsNoSpeaking (both of the above). |
| `BargeInMinWords` | int | `3` | Minimum user words required to interrupt when `BargeInType` is `3` or `4`. Guards against the AI interrupting itself on background noise transcribed as stray words. |

---

## <a id="convcontext"></a>🎭 ConversationContext

Personality + context fed to the LLM on every turn. Live-editable in the **Personality** panel.

| Field | Type | Default | Meaning |
|---|---|---|---|
| `SystemPromptFile` | string | `"personality.txt"` | File under `Resources/Prompts/` that holds the main system prompt. |
| `SystemPrompt` | string | `""` | Inline system prompt. When `UseCustomPrompt = true` this wins over the file. |
| `UseCustomPrompt` | bool | `false` | Prefer the inline `SystemPrompt` over the file. Toggle from the Personality panel's prompt-source selector. |
| `CurrentContext` | string | *(default story hook)* | Turn-level context / situation injected alongside the system prompt. Good for "today's scene" or short-term steering. |
| `Topics` | string[] | `["casual conversation"]` | List of topics the AI is aware of. Surfaced as steering hints. |

---

## 🔗 Back

➡️ Back to [INSTALLATION.md](./INSTALLATION.md) or the main [README.md](./README.md).
