<div align="center">
<h1>
⚙️ Installation and Setup Guide ⚙️
</h1>
<img src="assets/mascot_checklist.png" width="150" alt="Mascot with Checklist">
<p>Let's get your Live2D avatar ready for Persona Engine!</p>
</div>

> [!NOTE]
> For an overview of the project, its features, and use cases, please see the main [README.md](./README.md).

## 📜 Table of Contents

* [📋 Prerequisites: Let's Get Ready!](#prerequisites)
    * [System Requirements](#prereq-system)
    * [**MANDATORY:** Install NVIDIA CUDA & cuDNN](#prereq-cuda)
    * [Software to Install](#prereq-software)
    * [**MANDATORY:** Essential Models & Resources (Downloads!)](#prereq-models)
    * [Optional: RVC Models](#prereq-rvc)
    * [LLM Access](#prereq-llm)
    * [Spout Receiver](#prereq-spout)
* [🚀 Getting Started: Let's Go!](#getting-started)
    * [Easy Install (Recommended for Windows)](#install-release)
    * [Building from Source (Advanced)](#install-source)
* [🔧 Configuration (`appsettings.json`)](#configuration)
* [▶️ Usage: Showtime!](#usage)
* [🛠️ Troubleshooting](#troubleshooting)
    * [Still Stuck?](#still-stuck)
* [🔗 Back to Main README](./README.md)

---

## <a id="prerequisites"></a>📋 Prerequisites: Let's Get Ready!

 Before starting, ensure you have everything below. **An NVIDIA GPU with correctly installed CUDA and cuDNN is MANDATORY.**

### <a id="prereq-system"></a>1. System Requirements 🖥️

<details>
<summary><strong>➡️ Click here for detailed system notes...</strong></summary>

* 💻 **Operating System:**
    * **Windows (Strongly Recommended):** Developed and tested primarily on Windows 10/11. Pre-built releases are Windows-only.
    * Linux / macOS: Possible *only* by building from source. Needs significant technical expertise (CUDA, Spout alternatives, Audio linking) and is **not officially supported or tested**.
* 💪 **Graphics Card (GPU):**
    * **NVIDIA GPU with CUDA Support (MANDATORY):** **Absolutely essential.** ASR, TTS, and RVC rely heavily on CUDA via ONNX Runtime. Without a compatible NVIDIA GPU and correctly installed CUDA/cuDNN, the application **will not work**. Follow the **[CUDA & cuDNN Installation Guide](#prereq-cuda)** below precisely! Install the latest NVIDIA drivers.
    * CPU-Only / AMD / Intel GPUs: **Not supported.**
* 🎤 **Microphone:** Required for voice input.
* 🎧 **Speakers / Headphones:** Required for audio output.

</details>

---

### <a id="prereq-cuda"></a>2. 💪 **MANDATORY:** Install NVIDIA CUDA & cuDNN

> [!WARNING]
> **Failure to follow these CUDA/cuDNN steps *precisely*, especially the manual file copying (Step 3), is the most common cause of Persona Engine failing to start. Do not skip this section.**

This step is **non-negotiable**. The AI components require CUDA and cuDNN installed correctly. **Follow these steps carefully.** Failure *will* result in errors preventing the app from running.

<details>
<summary><strong>➡️ Click here for the REQUIRED CUDA + cuDNN Setup Guide (Windows)...</strong></summary>

1.  **Check GPU Compatibility & Install Driver:**
    * Ensure your NVIDIA GPU supports CUDA ([NVIDIA CUDA GPUs list](https://developer.nvidia.com/cuda-gpus)).
    * Get the **latest NVIDIA Game Ready or Studio driver** ([NVIDIA Driver Downloads](https://www.nvidia.com/Download/index.aspx)). A clean install is recommended.

2.  **Install CUDA Toolkit (Version 12.1 or 12.2 Recommended):**
    * The engine expects CUDA Runtime 12.1 or 12.2. **CUDA 12.2 is recommended**.
    * Go to the [NVIDIA CUDA Toolkit 12.2 Download Archive](https://developer.nvidia.com/cuda-12-2-0-download-archive).
    * Choose your system settings (Windows, x86_64, 10/11, `exe (local)`).
    * Download and run the installer. **Express (Recommended)** is usually fine.
    * Note the install path (e.g., `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.2`).

3.  **Install cuDNN Library (CRITICAL STEP - Manual Copy Required!):**
    * **You MUST download the TARBALL (`.tar.xz` or `.zip`) version, NOT the `.exe` installer.** The standard cuDNN installer often doesn't place files correctly for applications like this, so a manual copy is required.
    * Go to the [NVIDIA cuDNN Download Page](https://developer.nvidia.com/rdp/cudnn-download) (Requires free NVIDIA Developer account).
    * **Important:** Select the cuDNN version **compatible with your installed CUDA Toolkit**. For CUDA 12.2, choose **cuDNN v8.9.x or v9.x for CUDA 12.x**. (v9 recommended).
    * Download the "**Local Installer for Windows (Tar)**" or "(Zip)" file (e.g., `cudnn-windows-x86_64-9.x.x.x_cuda12-archive.zip`).
    * **Extract the cuDNN archive** (e.g., to Downloads). You'll find `bin`, `include`, `lib` folders.
    * **Manually copy the extracted files into your CUDA Toolkit installation directory:**
        * Copy **contents** of extracted cuDNN `bin` -> Your CUDA Toolkit `bin` folder (e.g., `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.2\bin`)
        * Copy **contents** of extracted cuDNN `include` -> Your CUDA Toolkit `include` folder (e.g., `...\CUDA\v12.2\include`)
        * Copy **contents** of extracted cuDNN `lib` (or `lib\x64`) -> Your CUDA Toolkit `lib\x64` folder (e.g., `...\CUDA\v12.2\lib\x64`)
        * *(Ensure `v12.2` matches your installed CUDA version!)*

4.  **Add CUDA Binaries to System Path (Important!):**
    * This allows Windows and the application to find the necessary CUDA libraries.
    * Search "Environment Variables" -> "Edit the system environment variables".
    * Click "Environment Variables...".
    * Under "System variables", find `Path` -> "Edit...".
    * Click "New" and add the path to your CUDA `bin` directory (use your actual version):
        * `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.2\bin`
    * *(Optional helpful: Add `libnvvp` path too)*
        * `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.2\libnvvp`
    * Click OK on all windows.

5.  **Restart Your Computer!** (**MANDATORY** for PATH changes to take effect)

6.  **Verification (Optional but Recommended):**
    * After restart, open Command Prompt (`cmd`).
    * Type `nvidia-smi`. It should show GPU details and CUDA version.
    * When running Persona Engine, watch the console for successful CUDA/ONNX initialization messages. Errors mentioning `cudnn64_*.dll` or `onnxruntime_providers_cuda.dll` indicate issues with Step 3 or 4. See [Troubleshooting](#troubleshooting).

</details>

---

### <a id="prereq-software"></a>3. 🛠️ Software to Install (Besides CUDA/Drivers)

Install these *before* running Persona Engine:

* **[.NET 9.0 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0):** Install the **Runtime** (not SDK unless building from source) system-wide. **MANDATORY.**
* **[`espeak-ng`](https://github.com/espeak-ng/espeak-ng/releases):** Required for TTS phonemization fallback (unknown words). **TTS may fail without it! MANDATORY.**
    1.  Go to `espeak-ng` releases page.
    2.  Download the latest Windows installer (`.msi`).
    3.  **Important:** During installation, **check the box "Add espeak-ng to the system PATH"**. This is easiest.
    4.  *Alternatively*: If not added to PATH, manually find install folder (e.g., `C:\Program Files\eSpeak NG`) and put its path (e.g., `"C:\\Program Files\\eSpeak NG"`) into `Config.Tts.EspeakPath` in `appsettings.json` (use double backslashes `\\`).

---

### <a id="prereq-models"></a>4. ❗ **MANDATORY:** Essential Models & Resources (Download Separately!) ❗

The release `.zip` includes TTS models, VAD, and the "Aria" avatar. **Whisper models need manual download**, and the `personality_example.txt` must be obtained from the repo if using standard LLMs.

* 🧠 **Whisper ASR Models (MANDATORY Download):**
    * **What:** AI models for Voice-to-Text (GGUF format required).
    * You need **both** for optimal performance:
        * `ggml-tiny.en.bin` (Faster, used for interruption detection)
        * `ggml-large-v3-turbo.bin` (Slower, more accurate, used for final transcription - Recommended!)
    * **Where:** Download **both** `.bin` files from:
        **[➡️ Download Whisper Models Here ⬅️](https://github.com/fagenorn/handcrafted-persona-engine/releases/tag/whisper_models)**
    * **Placement:** Place **both** downloaded `.bin` files directly into the 📁 `Resources/Models/` folder within your extracted Persona Engine directory.

* 🎭 **Live2D Avatar Model ("Aria" Included / Replaceable):**
    * **What:** Character files (`.model3.json`, textures, etc.). Rigging impacts animation quality.
    * **Included:** Demo avatar "Aria" in 📁 `Resources/Live2D/Avatars/aria/`. Default config points here.
    * **To use yours:**
        1.  **Review the [Live2D Integration & Rigging Guide](../Live2D.md)** for required parameters (esp. VBridger lip-sync) and setup. **MANDATORY** for custom avatars.
        2.  Create folder inside 📁 `Resources/Live2D/Avatars/` (e.g., `MyAvatar`), place model files inside.
        3.  Edit `appsettings.json`, change `Config.Live2D.ModelName` to your folder name (e.g., `"MyAvatar"`).

* 📝 **Personality Prompts (`personality.txt` & `personality_example.txt`):**
    * **What:** Text files instructing the LLM on character behavior. Critical for persona.
    * **`personality.txt`:** The **active** config file loaded by the engine. Located in 📁 `Resources/Prompts/` inside the *extracted application folder*. Initially optimized for the special fine-tuned model.
    * **`personality_example.txt`:** A **template/guide** for **standard** OpenAI-compatible LLMs (Ollama, Groq, etc.). Located in the *source code repository root on GitHub*, **not** in the release `.zip`. Get it from [GitHub](https://github.com/fagenorn/handcrafted-persona-engine).
    * **Action Required if using a Standard LLM (MANDATORY):** The default `personality.txt` (in release `.zip`) **will not work well** with standard models. You **must**:
        1.  Obtain `personality_example.txt` from the [source code repository](https://github.com/fagenorn/handcrafted-persona-engine/blob/main/personality_example.txt).
        2.  Open `personality.txt` in 📁 `Resources/Prompts/` inside your Persona Engine folder.
        3.  **Delete the default contents of `personality.txt`.**
        4.  **Copy the entire contents from `personality_example.txt` into the empty `personality.txt`**.
        5.  Customize the new `personality.txt` extensively for your character (requires prompt engineering).

* 🔊 **TTS Resources (Included in Release):** Files for speech synthesis in 📁 `Resources/Models/kokoro/`. No user action needed typically.
* 👂 **VAD Model (Included in Release):** Voice Activity Detection model (`silero_vad.onnx`) in 📁 `Resources/Models/`.

---

### <a id="prereq-rvc"></a>5. Optional: 👤 RVC Models (for Voice Cloning)

* **What:** For mimicking a specific voice. Requires an RVC model trained for that voice, **exported to ONNX format** (usually one `.onnx` file).
* **Note on `.pth` files:** Standard RVC `.pth` files **cannot be used directly** and must be converted to ONNX. This can be complex. Join Discord for help! 😊
* **Performance:** RVC adds CPU (pitch estimation) and GPU load. Disable if needed (`Config.Tts.Rvc.Enabled = false` in `appsettings.json`).
* **Placement:** Place converted `.onnx` file(s) in 📁 `Resources/Models/rvc/voice/`. Set `Config.Tts.Rvc.Enabled` to `true` and `Config.Tts.Rvc.DefaultVoice` to the filename (without extension) in `appsettings.json`.

---

### <a id="prereq-llm"></a>6. 🧠 LLM Access (The "Brain")

* **What:** Connect to an LLM service via an OpenAI-compatible API. **MANDATORY** configuration needed:
    * **API Endpoint URL:** Web address of LLM service (e.g., `http://localhost:11434/v1` for local Ollama, `https://api.groq.com/openai/v1` for Groq). Set in `Config.Llm.TextEndpoint`.
    * **(Optional) API Key:** Secret token if required (OpenAI, Groq). Set in `Config.Llm.TextApiKey`. Leave blank (`""`) if not needed.
    * **Model Name:** Identifier of the AI model (e.g., `gpt-4o`, `llama3`, `your-fine-tuned-id`). Set in `Config.Llm.TextModel`.
* **Options:**
    * **🏠 Local:** Ollama, LM Studio, Jan, etc. Often needs proxy like LiteLLM for `/v1` endpoint. Requires powerful PC (esp. GPU VRAM!).
    * **☁️ Cloud:** OpenAI, Groq, Anthropic, Together AI, etc. Requires account, API key, may incur costs.
* **Personality Reminder (MANDATORY):** Configure 📁 `Resources/Prompts/personality.txt` for your LLM type (use `personality_example.txt` from repo as guide for standard models - see [Prerequisites Section 4](#prereq-models)). Ask on Discord about the recommended fine-tuned model.

---

### <a id="prereq-spout"></a>7. 📺 Spout Receiver (To See Your Avatar!)

* **What:** Persona Engine outputs video via **Spout**. You need a separate app to receive this stream. **MANDATORY** to view the avatar.
* **Recommendation:** ✅ **OBS Studio** is highly recommended, especially for streaming/recording.
* **Required Plugin for OBS:** Install the **Spout2 Plugin for OBS Studio**. Download from: [https://github.com/Off-World-Live/obs-spout2-plugin/releases](https://github.com/Off-World-Live/obs-spout2-plugin/releases) (get correct version for your OBS). **MANDATORY** if using OBS.
* **How:** Install Spout2 plugin into OBS. After starting Persona Engine, add a "Spout2 Capture" source in OBS (details below). Multiple streams (Avatar, Roulette) can be configured in `appsettings.json`.

---

## <a id="getting-started"></a>🚀 Getting Started: Let's Go!

<div align="center">
<img src="assets/mascot_wrench.png" width="150" alt="Mascot with Wrench">
<p>Choose your installation path:</p>
</div>

### <a id="install-release"></a>Method 1: Easy Install with Pre-built Release (Recommended for Windows Users)

Simplest way on Windows.

**Step 1: 💾 Download & Extract Persona Engine**

<div align="center">
  <a href="https://github.com/fagenorn/handcrafted-persona-engine/releases/latest" target="_blank">
  <img
  src="assets/download.png"
  alt="Download Latest Release Button"
  width="300"
>
  </a>
  <p><i>(Click the button, get the `.zip` file from the latest release!)</i></p>
</div>

* Locate downloaded `.zip` (e.g., `PersonaEngine_vX.Y.Z.zip`).
* Right-click -> "Extract All...".
* **Recommended Location:** Simple path like `C:\PersonaEngine`. **Avoid** `C:\Program Files`, `C:\Windows`.

**Step 2: 🛠️ Install Prerequisites (MANDATORY - Do not skip!)**

* **NVIDIA Driver, CUDA, cuDNN:** **MANDATORY!** Ensure the **REQUIRED** guide was followed precisely ([Prerequisites Section 2](#prereq-cuda)), especially the **manual cuDNN copy from tar/zip** and the **final reboot**. Application **WILL NOT RUN** otherwise.
* **.NET 9.0 Runtime:** Installed system-wide? ([Prerequisites Section 3](#prereq-software)). **MANDATORY.**
* **`espeak-ng`:** Installed AND **added to system PATH** during install? ([Prerequisites Section 3](#prereq-software)). If not added to PATH, edit `appsettings.json` later. **MANDATORY.**

**Step 3: 📥 Download and Place Required Whisper Models (MANDATORY)**

* Go to Whisper Models download: **[➡️ Download Here ⬅️](https://github.com/fagenorn/handcrafted-persona-engine/releases/tag/whisper_models)**
* Download **both** `ggml-tiny.en.bin` and `ggml-large-v3-turbo.bin`.
* Navigate to extracted Persona Engine folder -> 📁 `Resources/Models/`.
* Place **both** `.bin` files inside `Resources/Models/`.

**Step 4: ⚙️ Initial Configuration (`appsettings.json` & `personality.txt`) (MANDATORY)**

* In extracted folder, open `appsettings.json` with a text editor (Notepad++, VS Code, etc.). **Refer to [Configuration](#configuration) section for structure.**
* **Verify/Edit:**
    * `Config.Llm`:
        * `TextEndpoint`: Set LLM API URL (e.g., `"https://api.groq.com/openai/v1"`). **MANDATORY.**
        * `TextModel`: Set LLM model name (e.g., `"llama-3.1-70b-versatile"`). **MANDATORY.**
        * `TextApiKey`: Enter API key if needed, else `""`.
    * `Config.Live2D`:
        * `ModelName`: Default `"aria"`. Change to `"YourModelFolder"` if using your own **correctly rigged** model placed in `Resources/Live2D/Avatars/YourModelFolder/`. (See [Live2D Guide](../Live2D.md)).
    * `(If needed) Config.Tts.EspeakPath`: If `espeak-ng` NOT added to PATH, set path here (e.g., `"C:\\Program Files\\eSpeak NG"`). Else, leave as `"espeak-ng"`.
* Save `appsettings.json`.
* <a id="configure-personality-txt---important"></a>**Configure Personality (`personality.txt`) - MANDATORY!**
    * Navigate to 📁 `Resources/Prompts/` in installation directory.
    * **Reminder:** If **not** using the special fine-tuned LLM (ask on Discord), the default `personality.txt` content is likely unsuitable for standard models.
    * **Action for Standard LLMs (MANDATORY):**
        1.  Go to [Persona Engine GitHub repo](https://github.com/fagenorn/handcrafted-persona-engine).
        2.  Find and open `personality_example.txt` in the root directory.
        3.  Copy its entire contents.
        4.  Open `personality.txt` in your `Resources/Prompts/` folder.
        5.  **Delete all default content** in `personality.txt`.
        6.  **Paste content copied from `personality_example.txt`** into the empty `personality.txt`.
        7.  **Edit `personality.txt` thoroughly.** Customize instructions, rules, character descriptions, examples for your specific persona. This requires prompt engineering.
    * Save `personality.txt`.

**Step 5: ▶️ Run Persona Engine!**

* Double-click `PersonaEngine.exe` in the main extracted folder.
* A **Configuration and Control UI** window appears (for settings/monitoring, **not the avatar**).
* A separate **console window** (black background) likely opens behind. **Watch console carefully** for startup messages. Look for confirmation: **CUDA/GPU detected and initialized successfully by ONNX Runtime**. Note errors, esp. `LoadLibrary failed` or cuDNN errors (see [Troubleshooting](#troubleshooting)). *The first launch might take longer as models are loaded.*

**Step 6: 📺 View the Avatar (via Spout in OBS) (MANDATORY to see avatar)**

* Requires Spout receiver (e.g., OBS).
* Ensure **OBS Studio** is installed.
* Ensure **Spout2 Plugin for OBS** is installed correctly ([Prerequisites Section 7](#prereq-spout)).
* Launch OBS Studio.
* In "Sources", click "+".
* Select **"Spout2 Capture"**.
* Name it (e.g., "Persona Engine Avatar") -> OK.
* Properties window: "Spout Sender" dropdown. Select sender for avatar (default: `"Live2D"`, check `Config.SpoutConfigs` in `appsettings.json`).
* Click OK. Avatar should appear in OBS scene! ✨ Resize/position.
* (Optional) Add another "Spout2 Capture" for Roulette Wheel if enabled (select its sender name, default `"RouletteWheel"`).
* *Note: If the "Spout Sender" dropdown in OBS is empty, Persona Engine likely didn't start correctly (check console) or the OBS plugin isn't working.*

**Step 7: 🔧 Further Customization (Optional)**

* Explore other `appsettings.json` settings (audio devices, TTS voice/speed/pitch, subtitles, RVC).
* Use **Configuration and Control UI** to adjust live TTS & Roulette Wheel settings. Check chat history panel.

---

### <a id="install-source"></a>Method 2: Building from Source (For Developers & Advanced Users 🛠️)

*(Requires .NET development familiarity.)*

1.  **Prerequisites:**
    * Install **Git**. **MANDATORY.**
    * Install **.NET 9.0 SDK** ([Microsoft](https://dotnet.microsoft.com/download/dotnet/9.0)). **MANDATORY.**
    * Install **`espeak-ng`** (+ added to PATH or path configured). ([Prerequisites Section 3](#prereq-software)). **MANDATORY.**
    * **MANDATORY:** Install **NVIDIA Driver, CUDA, cuDNN** following **REQUIRED** guide meticulously (**manual cuDNN copy from tar/zip!**). ([Prerequisites Section 2](#prereq-cuda)). **Reboot**.
    * Non-Windows: Needs equivalents for PortAudio, Spout (like Syphon), handling platform dependencies. **Unsupported territory!**
2.  **Clone Repository:**
    ```bash
    git clone [https://github.com/fagenorn/handcrafted-persona-engine.git](https://github.com/fagenorn/handcrafted-persona-engine.git)
    cd handcrafted-persona-engine
    ```
3.  **Restore Dependencies:** Open terminal in repo root:
    ```bash
    dotnet restore
    ```
4.  **Build Application:**
    ```bash
    # Example: Release build for Windows x64:
    dotnet publish PersonaEngine.App -c Release -r win-x64 -o ./publish --self-contained false
    # Adjust -r (runtime identifier) if needed (linux-x64, osx-x64 unsupported but possible)
    # --self-contained false relies on globally installed .NET Runtime
    ```
5.  **Navigate to Output:** Built files are in `./publish`.
6.  **Prepare Models & Resources (MANDATORY):**
    * Go into `./publish`.
    * Manually create: 📁 `Resources/Models/rvc/voice`, 📁 `Resources/Live2D/Avatars`, 📁 `Resources/Prompts`.
    * **Download Whisper GGUF models:** Get `ggml-tiny.en.bin`, `ggml-large-v3-turbo.bin` from [Whisper Models Release](https://github.com/fagenorn/handcrafted-persona-engine/releases/tag/whisper_models) -> place into `./publish/Resources/Models/`. **MANDATORY.**
    * **Copy Live2D Model:** Copy `Resources/Live2D/Avatars/aria` from repo -> `./publish/Resources/Live2D/Avatars/`. Or place your own custom-rigged model here (check [Live2D Guide](../Live2D.md)). **MANDATORY** (at least the default).
    * **Copy Core Models:** Copy contents of *original repo's* `Resources/Models` (incl. `kokoro`, `silero_vad.onnx`) -> `./publish/Resources/Models/`. **MANDATORY.**
    * **Copy Prompts:** Copy `personality.txt`, `personality_example.txt` from *original repo's* `Resources/Prompts` -> `./publish/Resources/Prompts/`. **MANDATORY.** Edit copied `personality.txt` based on `personality_example.txt` if using standard LLM (see step 7).
    * **Place RVC Models (Optional):** Place `.onnx` RVC model(s) -> `./publish/Resources/Models/rvc/voice/`.
7.  **Configure `appsettings.json` & `personality.txt` (MANDATORY):**
    * Copy `appsettings.json` from source project (`PersonaEngine.App/appsettings.json`) -> `./publish`.
    * Edit `./publish/appsettings.json`: Configure `Config.Llm` (Endpoint, Model, Key), `Config.Live2D.ModelName` ("aria" or custom), `Config.Tts.EspeakPath` if needed (as in Method 1, Step 4). **Refer to [Configuration](#configuration) section.** **MANDATORY.**
    * **Critically:** Edit `./publish/Resources/Prompts/personality.txt`. **If using standard LLM, replace content with `personality_example.txt`'s content** (copied in step 6), then customize extensively. **MANDATORY.**
8.  **Run Application:**
    * Open terminal inside `./publish`.
    * Execute: `dotnet PersonaEngine.App.dll`
    * Monitor console output for initialization (**CUDA status from ONNX Runtime**) and errors.
    * Set up Spout receiver (OBS) as in Method 1, Step 6. **MANDATORY** to see avatar.

---

## <a id="configuration"></a>🔧 Configuration (`appsettings.json`)

<div align="center">
<img src="assets/mascot_cog.png" width="150" alt="Mascot with Cog">
</div>

> [!NOTE]
> Always back up `appsettings.json` before making significant changes. Ensure correct JSON syntax (commas, quotes, braces `{}`, brackets `[]`).

Primary control panel JSON file in the main application folder (or `./publish` if built). Open with text editor. Changes usually require restart, **except for `Tts` and `RouletteWheel` sections (editable live via Control UI).**

Structure (default values shown):

```json
{
  "Config": {
    "Window": { // Basic window settings (less relevant due to Spout)
      "Width": 1920,
      "Height": 1080,
      "Title": "Persona Engine",
      "Fullscreen": false
    },
    "Llm": { // Large Language Model connection (MANDATORY)
      "TextApiKey": "gsk_...", // API Key (if required, else "")
      "TextModel": "llama-3.1-70b-versatile", // Model name (MANDATORY)
      "TextEndpoint": "https://api.groq.com/openai/v1", // API URL (MANDATORY)
      "VisionApiKey": "sk-...", // API Key for Vision model (if used)
      "VisionModel": "...", // Vision model name
      "VisionEndpoint": "http://..." // Vision model API URL
    },
    "Tts": { // Text-to-Speech (Live Editable via UI)
      "EspeakPath": "espeak-ng", // Path to espeak-ng install or "espeak-ng" if in PATH (MANDATORY)
      "Voice": { // Base TTS settings
        "DefaultVoice": "en_custom_2", // kokoro voice model name
        "UseBritishEnglish": false, // Pronunciation pref
        "DefaultSpeed": 1.0, // Speech rate (Live Editable)
        "MaxPhonemeLength": 510, // Internal buffer
        "SampleRate": 24000, // Audio sample rate
        "TrimSilence": false // Trim silence from ends
      },
      "Rvc": { // Real-time Voice Cloning (Optional)
        "DefaultVoice": "KasumiVA", // RVC .onnx model name (in Resources/Models/rvc/voice/)
        "Enabled": true, // Enable/Disable RVC (Live Editable)
        "HopSize": 64, // RVC processing param
        "SpeakerId": 0, // Speaker ID in RVC model
        "F0UpKey": 1 // Pitch shift (semitones) (Live Editable)
      }
    },
    "Subtitle": { // Subtitle appearance
      "Font": "DynaPuff_Condensed-Bold.ttf", // Font file (in Resources/Fonts/)
      "FontSize": 125,
      "Color": "#FFf8f6f7", // Text color (ARGB Hex)
      "HighlightColor": "#FFc4251e", // Highlight color
      "BottomMargin": 250, // Pixels from bottom
      "SideMargin": 30, // Pixels from sides
      "InterSegmentSpacing": 10, // Space between lines
      "MaxVisibleLines": 2, // Max lines shown
      "AnimationDuration": 0.3, // Fade in/out (seconds)
      "Width": 1080, // Canvas width
      "Height": 1920 // Canvas height
    },
    "Live2D": { // Live2D model settings
      "ModelPath": "Resources/Live2D/Avatars", // Base path
      "ModelName": "aria", // Folder name to load (MANDATORY)
      "Width": 1080, // Render target width
      "Height": 1920 // Render target height
    },
    "SpoutConfigs": [ // Spout video outputs (MANDATORY for output)
      {
        "OutputName": "Live2D", // Name OBS sees (MANDATORY)
        "Width": 1080,
        "Height": 1920
      },
      {
        "OutputName": "RouletteWheel", // Separate stream for wheel (Optional)
        "Width": 1080,
        "Height": 1080
      }
    ],
    "Vision": { // Experimental screen awareness
      "WindowTitle": "Microsoft Edge", // Window title to capture
      "Enabled": false, // Enable/Disable
      "CaptureInterval": "00:00:59", // How often (HH:MM:SS)
      "CaptureMinPixels": 50176, // Min window size
      "CaptureMaxPixels": 4194304 // Max window size
    },
    "RouletteWheel": { // Experimental roulette wheel (Live Editable via UI)
      "Font": "DynaPuff_Condensed-Bold.ttf",
      "FontSize": 24,
      "TextColor": "#FFFFFF",
      "TextScale": 1.0,
      "TextStroke": 2.0,
      "AdaptiveText": true,
      "RadialTextOrientation": true,
      "SectionLabels": [ "Yes", "No" ], // Wheel sections (Live Editable)
      "SpinDuration": 8.0, // Spin animation time (Live Editable)
      "MinRotations": 5.0, // Min rotations during spin (Live Editable)
      "WheelSizePercentage": 1.0, // Size relative to spout output
      "Width": 1080, // Render target width
      "Height": 1080, // Render target height
      "PositionMode": "Anchored", // Positioning
      "ViewportAnchor": "Center", // Anchor point
      "PositionXPercentage": 0.5, // X pos (0-1)
      "PositionYPercentage": 0.5, // Y pos (0-1)
      "AnchorOffsetX": 0,
      "AnchorOffsetY": 0,
      "AbsolutePositionX": 0,
      "AbsolutePositionY": 0,
      "Enabled": false, // Enable/Disable wheel (Live Editable)
      "RotationDegrees": -90.0, // Initial rotation
      "AnimateToggle": true, // Animate show/hide
      "AnimationDuration": 0.5 // Show/hide anim time
    }
  }
}
````

-----

## <a id="usage"></a>▶️ Usage: Showtime!

1.  **Double-check Prerequisites (MANDATORY)**:
    * Are **NVIDIA drivers/CUDA/cuDNN** installed correctly (using **manual tarball copy** per [Section 2](#prereq-cuda))?
    * Is the .NET Runtime installed?
    * Is `espeak-ng` installed (and either in your system PATH or the path explicitly set)?
    * Are the **Whisper `.bin` models** placed in the `Resources/Models/` directory?
    * Is your Spout receiver (e.g., OBS with the Spout Plugin) ready?

> [!CAUTION]
> The application will not run without a correctly configured CUDA environment.

2.  **Verify Configuration (MANDATORY)**:
    * Are the `Config.Llm` settings in `appsettings.json` correct for your setup?
    * Is `Config.Live2D.ModelName` set to the correct model name you intend to use?

3.  **Check Personality (MANDATORY)**:
    * Is the `Resources/Prompts/personality.txt` file configured appropriately for your **chosen LLM**?
    * (Remember to copy from `personality_example.txt` and customize if you are not using a pre-configured personality).

4.  **Run Application**:
    * Execute `PersonaEngine.exe` (if using the release build).
    * Or run `dotnet PersonaEngine.App.dll` (if running from source).

5.  **Monitor Startup**:
    * The **Config & Control UI** should appear.
    * Keep an eye on the **console window** running behind the UI.
    * **Look for "CUDA" / "ONNX Runtime" messages**; these confirm successful GPU initialization.
    * Errors during startup often indicate problems with the CUDA/cuDNN installation (see [Troubleshooting](#troubleshooting)).

6.  **Activate Spout Receiver (MANDATORY to see the avatar)**:
    * In OBS (or your chosen Spout receiver), add a "Spout2 Capture" source.
    * Select the sender name (default is `"Live2D"`). The avatar should now appear in the source.
    * Add other sources if needed (e.g., `"RouletteWheel"`).

7.  **Interact**:
    * Start talking! The processing flow is: VAD ⇢ Whisper ⇢ LLM ⇢ TTS/RVC ⇢ Audio Output ⇢ Subtitles ⇢ Live2D Animation.
    * You can monitor the transcription and LLM steps in the console or the UI.

8.  **Use Control UI**:
    * Monitor performance metrics.
    * View and edit the **Chat History**.
    * Adjust **TTS** settings (voice, speed, pitch, RVC model) live.
    * Control the **Roulette Wheel** feature.
-----

## <a id="troubleshooting"></a>🛠️ Troubleshooting

<div align="center">
<img src="assets/mascot_hardhat.png" width="150" alt="Mascot with Hardhat">
</div>

  * **CRITICAL Error: `DllNotFoundException` or `LoadLibrary failed ... error 126` (mentioning `onnxruntime_providers_cuda.dll`, `cublas64_*.dll`, `cudnn64_*.dll`, etc.)**

      * **Cause:** Almost always incorrect **CUDA or cuDNN installation** or missing dependencies. Persona Engine **cannot run**.
      * **Solution:** Likely did not follow **manual install steps for cuDNN using tar/zip** OR CUDA install/PATH is wrong.
        1.  Go back to **[REQUIRED CUDA + cuDNN Setup Guide](#prereq-cuda)**.
        2.  **Re-do Step 3 (Install cuDNN)**. Ensure downloaded **TAR/ZIP** cuDNN compatible with CUDA 12.x (v9 recommended).
        3.  **Manually copy** files from extracted cuDNN `bin`, `include`, `lib` ⇢ corresponding CUDA Toolkit folders (e.g., `...\CUDA\v12.2\`).
        4.  Ensure CUDA `bin` path (e.g., `...\CUDA\v12.2\bin`) is in **System Environment `Path`** (Step 4).
        5.  **Restart computer** (Step 5) is **MANDATORY**.
        6.  Run again, check console logs for CUDA/ONNX init messages. Check the *exact* DLL name mentioned in the error; it often points directly to whether it's a core CUDA issue (`cublas*.dll`), a cuDNN issue (`cudnn*.dll`), or the ONNX runtime bridge (`onnxruntime_providers_cuda.dll`).

  * **TTS Silent or Crashes App**

      * **Cause 1:** `espeak-ng` not installed or accessible (PATH / `Config.Tts.EspeakPath`).
      * **Solution 1:** Install `espeak-ng` ([Prerequisites Section 3](#prereq-software)). **Check "Add to system PATH" during install**. If missed, reinstall OR set full path (e.g., `"C:\\Program Files\\eSpeak NG"`) in `Config.Tts.EspeakPath` in `appsettings.json`, restart app.
      * **Cause 2:** TTS models (`kokoro`) missing/corrupted.
      * **Solution 2:** Ensure `Resources/Models/kokoro` folder exists with necessary files (from release `.zip` or copied if built from source).
      * **Cause 3:** Incorrect audio output device selected in Windows.
      * **Solution 3:** Ensure the correct audio output device is selected in Windows sound settings and is working.

  * **App Crashes on Startup / When Speaking (Whisper/ASR Issue)**

      * **Cause:** Required Whisper `.bin` models missing or wrong location.
      * **Solution:** Confirm download of **both** `ggml-tiny.en.bin` AND `ggml-large-v3-turbo.bin` from [Whisper Models Release](https://github.com/fagenorn/handcrafted-persona-engine/releases/tag/whisper_models). Place **directly** inside `Resources/Models/`.

  * **No Response from LLM / LLM Errors**

      * **Cause 1:** Incorrect LLM config in `appsettings.json`.
      * **Solution 1:** Check `Config.Llm.TextEndpoint` URL (reachable?). `Config.Llm.TextModel` exact? `Config.Llm.TextApiKey` correct (or `""`)?
      * **Cause 2:** LLM service down/issue.
      * **Solution 2:** Check LLM provider status (cloud) or ensure local server (Ollama) is running & accessible. Check the console/UI logs for the *exact* error message returned by the LLM API, as this often indicates the problem (e.g., 'authentication failed', 'model not found', 'rate limit exceeded').
      * **Cause 3:** Badly formatted `personality.txt` for the LLM.
      * **Solution 3:** If standard LLM, ensure `personality.txt` based on `personality_example.txt` (from repo). Simplify prompt to test.

  * **Avatar Not Appearing in OBS / Spout Receiver**

      * **Cause 1:** Persona Engine not running or failed Spout init (check console).
      * **Solution 1:** Ensure `PersonaEngine.exe` running. Check console logs for Spout errors after CUDA/ONNX checks.
      * **Cause 2:** Spout2 Plugin for OBS not installed/loaded.
      * **Solution 2:** Reinstall [Spout2 Plugin for OBS](https://github.com/Off-World-Live/obs-spout2-plugin/releases) for your OBS version. Restart OBS.
      * **Cause 3:** Incorrect Spout source config in OBS.
      * **Solution 3:** In OBS, remove/re-add "Spout2 Capture". Check "Spout Sender" dropdown. Does correct name (`"Live2D"`, `"RouletteWheel"`) appear? If not, issue with Persona Engine Spout init or OBS plugin.
      * **Cause 4:** Firewall blocking (less common locally).
      * **Solution 4:** Temporarily disable firewall to test. If works, create rules for OBS/Persona Engine.

  * **General Tip:** Try restarting the application and potentially your computer after making configuration changes or encountering persistent errors.

-----

### <a id="still-stuck"></a>Still Stuck?

<img src="assets/mascot_sigh.png" alt="Mascot looking puzzled" width="150" align="right">

  * Check **console window** and **Control UI logs** for detailed error messages!
  * Join our [**Discord Community**](https://discord.gg/p3CXEyFtrA)! Ask for help in support channels, providing:
      * What you tried.
      * What happened (specific error messages).
      * Windows version.
      * **NVIDIA GPU model**.
      * LLM you're connecting to.
      * Confirmation you followed CUDA/cuDNN install guide precisely, including **manual file copy and reboot**.

-----

## <a id="back-to-main-readme"></a>🔗 Back to Main README

➡️ **Return to the main [README.md](./README.md) for project overview, features, and use cases.**