using System.Diagnostics;
using System.Numerics;
using System.Threading.Channels;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Widgets.Dialogs;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.TTS.RVC;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Engine;
using PersonaEngine.Lib.TTS.Synthesis.Kokoro;
using PersonaEngine.Lib.TTS.Synthesis.Qwen3;

namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Editor for TTS section configuration
/// </summary>
public class TtsConfigEditor : ConfigSectionEditorBase
{
    private readonly IAudioOutputAdapter _audioPlayer;

    private readonly ITtsEngineProvider _engineProvider;

    private readonly IRVCVoiceProvider _rvcProvider;

    private readonly IUiThemeManager _themeManager;

    private readonly ITtsEngine _ttsEngine;

    private readonly IKokoroVoiceProvider _voiceProvider;

    private readonly IQwen3VoiceProvider _qwen3VoiceProvider;

    // Engine selection
    private string _activeEngine;

    private List<string> _availableQwen3Speakers = new();

    private List<string> _availableRVCs = new();

    private List<string> _availableVoices = new();

    private TtsConfiguration _currentConfig;

    private Qwen3TtsOptions _currentQwen3Options;

    private RVCFilterOptions _currentRvcFilterOptions;

    private KokoroVoiceOptions _currentVoiceOptions;

    private string _defaultRVC;

    private string _defaultVoice;

    private string _espeakPath;

    private bool _isPlaying = false;

    private bool _loadingQwen3Speakers = false;

    private bool _loadingRvcs = false;

    private bool _loadingVoices = false;

    private int _maxPhonemeLength;

    private string _modelDir;

    private ActiveOperation? _playbackOperation = null;

    // Qwen3 fields
    private string _qwen3Instruct = "";

    private string _qwen3Language;

    private int _qwen3MaxNewTokens;

    private float _qwen3RepetitionPenalty;

    private string _qwen3Speaker;

    private float _qwen3Temperature;

    private int _qwen3TopK;

    private float _qwen3TopP;

    private bool _qwen3CodePredictorGreedy;

    private bool _qwen3SilencePenaltyEnabled;

    private bool _rvcEnabled;

    private int _rvcF0UpKey;

    private int _rvcHopSize;

    private int _sampleRate;

    private float _speechRate;

    private string _testText = "This is a test of the text-to-speech system.";

    private bool _trimSilence;

    private bool _useBritishEnglish;

    public TtsConfigEditor(
        IUiConfigurationManager configManager,
        IEditorStateManager stateManager,
        ITtsEngine ttsEngine,
        ITtsEngineProvider engineProvider,
        IOutputAdapter audioPlayer,
        IKokoroVoiceProvider voiceProvider,
        IQwen3VoiceProvider qwen3VoiceProvider,
        IUiThemeManager themeManager,
        IRVCVoiceProvider rvcProvider
    )
        : base(configManager, stateManager)
    {
        _ttsEngine = ttsEngine;
        _engineProvider = engineProvider;
        _audioPlayer = (IAudioOutputAdapter)audioPlayer;
        _voiceProvider = voiceProvider;
        _qwen3VoiceProvider = qwen3VoiceProvider;
        _themeManager = themeManager;
        _rvcProvider = rvcProvider;

        LoadConfiguration();

        // _audioPlayerHost.OnPlaybackStarted   += OnPlaybackStarted;
        // _audioPlayerHost.OnPlaybackCompleted += OnPlaybackCompleted;
    }

    public override string SectionKey => "TTS";

    public override string DisplayName => "TTS Configuration";

    public override void Initialize()
    {
        // Load available voices
        LoadAvailableVoicesAsync();
        LoadAvailableRVCAsync();
        LoadAvailableQwen3SpeakersAsync();
    }

    public override void Render()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        // Main layout with tabs for different sections
        if (ImGui.BeginTabBar("TtsConfigTabs"))
        {
            // Basic settings tab
            if (ImGui.BeginTabItem("Basic Settings"))
            {
                RenderTestingSection();
                RenderBasicSettings();

                ImGui.EndTabItem();
            }

            // Advanced settings tab
            if (ImGui.BeginTabItem("Advanced Settings"))
            {
                RenderAdvancedSettings();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Reset button at bottom
        ImGui.SetCursorPosX(availWidth * .5f * .5f);
        if (ImGui.Button("Reset", new Vector2(150, 0)))
        {
            ResetToDefaults();
        }

        ImGui.SameLine(0, 10);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Reset all TTS settings to default values");
        }

        if (!StateManager.HasUnsavedChanges)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Save", new Vector2(150, 0));
            ImGui.EndDisabled();
        }
        else if (ImGui.Button("Save", new Vector2(150, 0)))
        {
            SaveConfiguration();
        }
    }

    public override void Update(float deltaTime)
    {
        // Simplified update just for playback operation
        if (_playbackOperation != null && _isPlaying)
        {
            _playbackOperation.Progress += deltaTime * 0.1f;
            if (_playbackOperation.Progress > 0.99f)
            {
                _playbackOperation.Progress = 0.99f;
            }
        }
    }

    public override void OnConfigurationChanged(ConfigurationChangedEventArgs args)
    {
        base.OnConfigurationChanged(args);

        if (args.Type == ConfigurationChangedEventArgs.ChangeType.Reloaded)
        {
            LoadConfiguration();
        }
    }

    public override void Dispose()
    {
        // Unsubscribe from audio player events
        // _audioPlayerHost.OnPlaybackStarted   -= OnPlaybackStarted;
        // _audioPlayerHost.OnPlaybackCompleted -= OnPlaybackCompleted;

        // Cancel any active playback
        StopPlayback();

        base.Dispose();
    }

    #region Configuration Management

    private void LoadConfiguration()
    {
        _currentConfig = ConfigManager.GetConfiguration<TtsConfiguration>("TTS");
        _currentVoiceOptions = _currentConfig.Kokoro;
        _currentRvcFilterOptions = _currentConfig.Rvc;
        _currentQwen3Options = _currentConfig.Qwen3;

        // Engine selection
        _activeEngine = _currentConfig.ActiveEngine;

        // Kokoro fields
        _modelDir = _currentConfig.ModelDirectory;
        _espeakPath = _currentConfig.EspeakPath;
        _speechRate = _currentVoiceOptions.DefaultSpeed;
        _sampleRate = _currentVoiceOptions.SampleRate;
        _trimSilence = _currentVoiceOptions.TrimSilence;
        _useBritishEnglish = _currentVoiceOptions.UseBritishEnglish;
        _defaultVoice = _currentVoiceOptions.DefaultVoice;
        _maxPhonemeLength = _currentVoiceOptions.MaxPhonemeLength;

        // Qwen3 fields
        _qwen3Speaker = _currentQwen3Options.Speaker;
        _qwen3Language = _currentQwen3Options.Language;
        _qwen3Instruct = _currentQwen3Options.Instruct ?? "";
        _qwen3Temperature = _currentQwen3Options.Temperature;
        _qwen3TopK = _currentQwen3Options.TopK;
        _qwen3TopP = _currentQwen3Options.TopP;
        _qwen3RepetitionPenalty = _currentQwen3Options.RepetitionPenalty;
        _qwen3MaxNewTokens = _currentQwen3Options.MaxNewTokens;
        _qwen3CodePredictorGreedy = _currentQwen3Options.CodePredictorGreedy;
        _qwen3SilencePenaltyEnabled = _currentQwen3Options.SilencePenaltyEnabled;

        // RVC
        _defaultRVC = _currentRvcFilterOptions.DefaultVoice;
        _rvcEnabled = _currentRvcFilterOptions.Enabled;
        _rvcHopSize = _currentRvcFilterOptions.HopSize;
        _rvcF0UpKey = _currentRvcFilterOptions.F0UpKey;
    }

    private async void LoadAvailableVoicesAsync()
    {
        try
        {
            _loadingVoices = true;

            // Register an active operation
            var operation = new ActiveOperation("load-voices", "Loading Voices");
            StateManager.RegisterActiveOperation(operation);

            // Load voices asynchronously
            var voices = _voiceProvider.GetAvailableVoices();
            _availableVoices = voices.ToList();

            // Clear operation
            StateManager.ClearActiveOperation(operation.Id);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading voices: {ex.Message}");
            _availableVoices = [];
        }
        finally
        {
            _loadingVoices = false;
        }
    }

    private async void LoadAvailableRVCAsync()
    {
        try
        {
            _loadingRvcs = true;

            // Register an active operation
            var operation = new ActiveOperation("load-rvc", "Loading RVC Voices");
            StateManager.RegisterActiveOperation(operation);

            // Load voices asynchronously
            var voices = _rvcProvider.GetAvailableVoices();
            _availableRVCs = voices.ToList();

            // Clear operation
            StateManager.ClearActiveOperation(operation.Id);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading voices: {ex.Message}");
            _availableRVCs = [];
        }
        finally
        {
            _loadingRvcs = false;
        }
    }

    private async void LoadAvailableQwen3SpeakersAsync()
    {
        try
        {
            _loadingQwen3Speakers = true;

            var operation = new ActiveOperation("load-qwen3-speakers", "Loading Qwen3 Speakers");
            StateManager.RegisterActiveOperation(operation);

            var speakers = _qwen3VoiceProvider.GetAvailableSpeakers();
            _availableQwen3Speakers = speakers.ToList();

            StateManager.ClearActiveOperation(operation.Id);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading Qwen3 speakers: {ex.Message}");
            _availableQwen3Speakers = [];
        }
        finally
        {
            _loadingQwen3Speakers = false;
        }
    }

    private void UpdateConfiguration()
    {
        var updatedVoiceOptions = new KokoroVoiceOptions
        {
            DefaultVoice = _defaultVoice,
            DefaultSpeed = _speechRate,
            SampleRate = _sampleRate,
            TrimSilence = _trimSilence,
            UseBritishEnglish = _useBritishEnglish,
            MaxPhonemeLength = _maxPhonemeLength,
        };

        var updatedRVCOptions = new RVCFilterOptions
        {
            DefaultVoice = _defaultRVC,
            Enabled = _rvcEnabled,
            HopSize = _rvcHopSize,
            F0UpKey = _rvcF0UpKey,
        };

        var updatedQwen3Options = new Qwen3TtsOptions
        {
            Speaker = _qwen3Speaker,
            Language = _qwen3Language,
            Instruct = string.IsNullOrWhiteSpace(_qwen3Instruct) ? null : _qwen3Instruct,
            Temperature = _qwen3Temperature,
            TopK = _qwen3TopK,
            TopP = _qwen3TopP,
            RepetitionPenalty = _qwen3RepetitionPenalty,
            MaxNewTokens = _qwen3MaxNewTokens,
            EmitEveryFrames = _currentQwen3Options.EmitEveryFrames,
            CodePredictorGreedy = _qwen3CodePredictorGreedy,
            SilencePenaltyEnabled = _qwen3SilencePenaltyEnabled,
        };

        var updatedConfig = new TtsConfiguration
        {
            ActiveEngine = _activeEngine,
            ModelDirectory = _modelDir,
            EspeakPath = _espeakPath,
            Kokoro = updatedVoiceOptions,
            Rvc = updatedRVCOptions,
            Qwen3 = updatedQwen3Options,
        };

        _currentRvcFilterOptions = updatedRVCOptions;
        _currentVoiceOptions = updatedVoiceOptions;
        _currentQwen3Options = updatedQwen3Options;
        _currentConfig = updatedConfig;
        ConfigManager.UpdateConfiguration(updatedConfig, SectionKey);

        MarkAsChanged();
    }

    private void SaveConfiguration()
    {
        ConfigManager.SaveConfiguration();
        MarkAsSaved();
    }

    private void ResetToDefaults()
    {
        var defaultConfig = new TtsConfiguration();
        var defaultVoiceOptions = defaultConfig.Kokoro;
        var defaultQwen3Options = defaultConfig.Qwen3;

        // Update local state
        _currentConfig = defaultConfig;
        _currentVoiceOptions = defaultVoiceOptions;
        _currentQwen3Options = defaultQwen3Options;

        // Engine
        _activeEngine = defaultConfig.ActiveEngine;

        // Kokoro fields
        _modelDir = defaultConfig.ModelDirectory;
        _espeakPath = defaultConfig.EspeakPath;
        _speechRate = defaultVoiceOptions.DefaultSpeed;
        _sampleRate = defaultVoiceOptions.SampleRate;
        _trimSilence = defaultVoiceOptions.TrimSilence;
        _useBritishEnglish = defaultVoiceOptions.UseBritishEnglish;
        _defaultVoice = defaultVoiceOptions.DefaultVoice;
        _maxPhonemeLength = defaultVoiceOptions.MaxPhonemeLength;

        // Qwen3 fields
        _qwen3Speaker = defaultQwen3Options.Speaker;
        _qwen3Language = defaultQwen3Options.Language;
        _qwen3Instruct = defaultQwen3Options.Instruct ?? "";
        _qwen3Temperature = defaultQwen3Options.Temperature;
        _qwen3TopK = defaultQwen3Options.TopK;
        _qwen3TopP = defaultQwen3Options.TopP;
        _qwen3RepetitionPenalty = defaultQwen3Options.RepetitionPenalty;
        _qwen3MaxNewTokens = defaultQwen3Options.MaxNewTokens;
        _qwen3CodePredictorGreedy = defaultQwen3Options.CodePredictorGreedy;
        _qwen3SilencePenaltyEnabled = defaultQwen3Options.SilencePenaltyEnabled;

        // Update configuration
        ConfigManager.UpdateConfiguration(defaultConfig, "TTS");
        MarkAsChanged();
    }

    #endregion

    #region Playback Controls

    private async void StartPlayback()
    {
        if (_isPlaying || string.IsNullOrWhiteSpace(_testText))
        {
            return;
        }

        try
        {
            // Create a new playback operation
            _playbackOperation = new ActiveOperation("tts-playback", "Playing TTS");
            StateManager.RegisterActiveOperation(_playbackOperation);

            _isPlaying = true;

            var llmInput = Channel.CreateUnbounded<LlmChunkEvent>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
            );
            var ttsOutput = Channel.CreateUnbounded<IOutputEvent>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
            );
            var audioInput = Channel.CreateUnbounded<TtsChunkEvent>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
            );
            var audioEvents = Channel.CreateUnbounded<IOutputEvent>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
            ); // Tts Started/Tts Ended - Audio Started/Audio Ended

            await llmInput.Writer.WriteAsync(
                new LlmChunkEvent(Guid.Empty, Guid.Empty, DateTimeOffset.UtcNow, _testText),
                _playbackOperation.CancellationSource.Token
            );
            llmInput.Writer.Complete();

            _ = Task.Run(async () =>
            {
                await foreach (
                    var ttsOut in ttsOutput.Reader.ReadAllAsync(
                        _playbackOperation.CancellationSource.Token
                    )
                )
                {
                    if (ttsOut is TtsChunkEvent ttsChunk)
                    {
                        await audioInput.Writer.WriteAsync(
                            ttsChunk,
                            _playbackOperation.CancellationSource.Token
                        );
                    }
                }
            });

            _ = _ttsEngine.SynthesizeStreamingAsync(
                llmInput,
                ttsOutput,
                Guid.Empty,
                Guid.Empty,
                _playbackOperation.CancellationSource.Token
            );

            await _audioPlayer.SendAsync(
                audioInput,
                audioEvents,
                Guid.Empty,
                _playbackOperation.CancellationSource.Token
            );
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("TTS playback cancelled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during TTS playback: {ex.Message}");
            _isPlaying = false;

            if (_playbackOperation != null)
            {
                StateManager.ClearActiveOperation(_playbackOperation.Id);
                _playbackOperation = null;
            }
        }
    }

    private void StopPlayback()
    {
        if (_playbackOperation != null)
        {
            _playbackOperation.CancellationSource.Cancel();
            StateManager.ClearActiveOperation(_playbackOperation.Id);
            _playbackOperation = null;
        }

        _isPlaying = false;
    }

    private void OnPlaybackStarted(object sender, EventArgs args)
    {
        _isPlaying = true;

        if (_playbackOperation != null)
        {
            _playbackOperation.Progress = 0.0f;
        }
    }

    private void OnPlaybackCompleted(object sender, EventArgs args)
    {
        _isPlaying = false;

        if (_playbackOperation != null)
        {
            _playbackOperation.Progress = 1.0f;
            StateManager.ClearActiveOperation(_playbackOperation.Id);
            _playbackOperation = null;
        }
    }

    #endregion

    #region UI Rendering Methods

    private void RenderTestingSection()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        ImGui.Spacing();
        ImGui.SeparatorText("Playground");
        ImGui.Spacing();

        // Text input frame with light background
        ImGui.Text("Test Text:");
        ImGui.SetNextItemWidth(availWidth);
        ImGui.InputTextMultiline("##TestText", ref _testText, 1000, new Vector2(0, 80));

        ImGui.Spacing();
        ImGui.Spacing();

        // Controls section with better layout
        ImGui.BeginGroup();
        {
            // Left side: Example selector
            var controlWidth = Math.Min(180, availWidth * 0.4f);
            ImGui.SetNextItemWidth(controlWidth);

            if (ImGui.BeginCombo("##exampleLbl", "Select Example"))
            {
                string[] examples =
                {
                    "Hello, world!",
                    "The quick brown fox jumps over the lazy dog.",
                    "Welcome to the text-to-speech system.",
                    "How are you doing today?",
                    "Today's date is March 3rd, 2025.",
                };

                foreach (var example in examples)
                {
                    var isSelected = _testText == example;
                    if (ImGui.Selectable(example, ref isSelected))
                    {
                        _testText = example;
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine(0, 15);

            var clearDisabled = string.IsNullOrEmpty(_testText);
            if (clearDisabled)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Clear", new Vector2(80, 0)) && !string.IsNullOrEmpty(_testText))
            {
                _testText = "";
            }

            if (clearDisabled)
            {
                ImGui.EndDisabled();
            }
        }

        ImGui.EndGroup();

        ImGui.SameLine(0, 10);

        // Playback controls in a styled frame
        {
            // Play/Stop button with color styling
            if (_isPlaying)
            {
                if (UiStyler.AnimatedButton("Stop", new Vector2(80, 0), _isPlaying))
                {
                    StopPlayback();
                }

                ImGui.SameLine(0, 15);
                ImGui.ProgressBar(_playbackOperation?.Progress ?? 0, new Vector2(-1, 0), "Playing");
            }
            else
            {
                var disabled = string.IsNullOrWhiteSpace(_testText);
                if (disabled)
                {
                    ImGui.BeginDisabled();
                }

                if (UiStyler.AnimatedButton("Play", new Vector2(80, 0), _isPlaying))
                {
                    StartPlayback();
                }

                if (disabled)
                {
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Enter some text to play");
                    }
                }
            }
        }
    }

    private void RenderBasicSettings()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        // === Engine Selection ===
        ImGui.Spacing();
        ImGui.SeparatorText("TTS Engine");
        ImGui.Spacing();

        ImGui.BeginGroup();
        {
            var engines = _engineProvider.AvailableEngines;
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Engine:");
            ImGui.SameLine(120);
            ImGui.SetNextItemWidth(availWidth - 120);

            if (ImGui.BeginCombo("##EngineSelector", _activeEngine))
            {
                foreach (var engine in engines)
                {
                    var isSelected = engine.EngineId == _activeEngine;
                    if (ImGui.Selectable(engine.EngineId, isSelected))
                    {
                        _activeEngine = engine.EngineId;
                        UpdateConfiguration();
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }
        }

        ImGui.EndGroup();

        // === Engine-specific settings ===
        if (string.Equals(_activeEngine, "kokoro", StringComparison.OrdinalIgnoreCase))
        {
            RenderKokoroSettings(availWidth);
        }
        else if (string.Equals(_activeEngine, "qwen3", StringComparison.OrdinalIgnoreCase))
        {
            RenderQwen3Settings(availWidth);
        }

        // === RVC Selection Section ===
        ImGui.Spacing();
        ImGui.SeparatorText("RVC Settings");
        ImGui.Spacing();

        ImGui.BeginGroup();
        {
            var rvcConfigChanged = ImGui.Checkbox("Enable Voice Change", ref _rvcEnabled);

            ImGui.SetNextItemWidth(availWidth - 120);

            if (!_rvcEnabled)
            {
                ImGui.BeginDisabled();
            }

            if (_loadingVoices)
            {
                ImGui.BeginDisabled();
                var loadingText = "Loading voices...";
                ImGui.InputText("##RVCLoading", ref loadingText, 100, ImGuiInputTextFlags.ReadOnly);
                ImGui.EndDisabled();
            }
            else
            {
                if (
                    ImGui.BeginCombo(
                        "##RVCSelector",
                        string.IsNullOrEmpty(_defaultRVC) ? "<Select voice>" : _defaultRVC
                    )
                )
                {
                    if (_availableRVCs.Count == 0)
                    {
                        ImGui.TextColored(
                            new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                            "No voices available"
                        );
                    }
                    else
                    {
                        foreach (var voice in _availableRVCs)
                        {
                            var isSelected = voice == _defaultRVC;
                            if (ImGui.Selectable(voice, isSelected))
                            {
                                _defaultRVC = voice;
                                UpdateConfiguration();
                            }

                            if (isSelected)
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                    }

                    ImGui.EndCombo();
                }
            }

            ImGui.SameLine(0, 10);

            if (ImGui.Button("Refresh##RefreshRVC", new Vector2(-1, 0)))
            {
                LoadAvailableRVCAsync();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Refresh available voices list");
            }

            if (ImGui.BeginTable("RVCProps", 4, ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("1", 100f);
                ImGui.TableSetupColumn("2", 200f);
                ImGui.TableSetupColumn("3", 100f);
                ImGui.TableSetupColumn("4", 200f);

                // Hop Size
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Hop Size");
                ImGui.TableNextColumn();
                var fontSizeChanged = ImGui.InputInt("##HopSize", ref _rvcHopSize, 8);
                if (fontSizeChanged)
                {
                    _rvcHopSize = Math.Clamp(_rvcHopSize, 8, 256);
                    rvcConfigChanged = true;
                }

                // F0 Key
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Pitch");
                ImGui.TableNextColumn();
                var pitchChanged = ImGui.InputInt("##Pitch", ref _rvcF0UpKey, 1);
                if (pitchChanged)
                {
                    _rvcF0UpKey = Math.Clamp(_rvcF0UpKey, -20, 20);
                    rvcConfigChanged = true;
                }

                ImGui.EndTable();
            }

            if (!_rvcEnabled)
            {
                ImGui.EndDisabled();
            }

            if (rvcConfigChanged)
            {
                UpdateConfiguration();
            }
        }

        ImGui.EndGroup();
    }

    private void RenderKokoroSettings(float availWidth)
    {
        ImGui.Spacing();
        ImGui.SeparatorText("Kokoro Voice Settings");
        ImGui.Spacing();

        ImGui.BeginGroup();
        {
            ImGui.SetNextItemWidth(availWidth - 120);

            if (_loadingVoices)
            {
                ImGui.BeginDisabled();
                var loadingText = "Loading voices...";
                ImGui.InputText(
                    "##VoiceLoading",
                    ref loadingText,
                    100,
                    ImGuiInputTextFlags.ReadOnly
                );
                ImGui.EndDisabled();
            }
            else
            {
                if (
                    ImGui.BeginCombo(
                        "##VoiceSelector",
                        string.IsNullOrEmpty(_defaultVoice) ? "<Select voice>" : _defaultVoice
                    )
                )
                {
                    if (_availableVoices.Count == 0)
                    {
                        ImGui.TextColored(
                            new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                            "No voices available"
                        );
                    }
                    else
                    {
                        foreach (var voice in _availableVoices)
                        {
                            var isSelected = voice == _defaultVoice;
                            if (ImGui.Selectable(voice, isSelected))
                            {
                                _defaultVoice = voice;
                                UpdateConfiguration();
                            }

                            if (isSelected)
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                    }

                    ImGui.EndCombo();
                }
            }

            ImGui.SameLine(0, 10);

            if (ImGui.Button("Refresh##VoiceRefresh", new Vector2(-1, 0)))
            {
                LoadAvailableVoicesAsync();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Refresh available voices list");
            }

            // Speech rate slider
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Speech Rate:");
            ImGui.SameLine(120);

            ImGui.SetNextItemWidth(availWidth - 120 - 120);
            var rateChanged = ImGui.SliderFloat(
                "##SpeechRate",
                ref _speechRate,
                0.5f,
                2.0f,
                "%.2f"
            );

            ImGui.SameLine(0, 10);

            if (ImGui.Button("Reset##Rate", new Vector2(-1, 0)))
            {
                _speechRate = 1.0f;
                rateChanged = true;
            }

            // Voice options
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Options:");
            ImGui.SameLine(120);

            var trimChanged = ImGui.Checkbox("Trim Silence", ref _trimSilence);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Remove silence from beginning and end of speech");
            }

            ImGui.SameLine(0, 50);

            var britishChanged = ImGui.Checkbox("Use British English", ref _useBritishEnglish);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Use British English pronunciation instead of American English");
            }

            if (rateChanged || trimChanged || britishChanged)
            {
                UpdateConfiguration();
            }
        }

        ImGui.EndGroup();
    }

    private void RenderQwen3Settings(float availWidth)
    {
        ImGui.Spacing();
        ImGui.SeparatorText("Qwen3 Voice Settings");
        ImGui.Spacing();

        var changed = false;

        ImGui.BeginGroup();
        {
            // Speaker dropdown
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Speaker:");
            ImGui.SameLine(120);
            ImGui.SetNextItemWidth(availWidth - 120 - 80);

            if (_loadingQwen3Speakers)
            {
                ImGui.BeginDisabled();
                var loadingText = "Loading speakers...";
                ImGui.InputText(
                    "##Qwen3SpeakerLoading",
                    ref loadingText,
                    100,
                    ImGuiInputTextFlags.ReadOnly
                );
                ImGui.EndDisabled();
            }
            else
            {
                if (
                    ImGui.BeginCombo(
                        "##Qwen3SpeakerSelector",
                        string.IsNullOrEmpty(_qwen3Speaker) ? "<Select speaker>" : _qwen3Speaker
                    )
                )
                {
                    if (_availableQwen3Speakers.Count == 0)
                    {
                        ImGui.TextColored(
                            new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                            "No speakers available"
                        );
                    }
                    else
                    {
                        foreach (var speaker in _availableQwen3Speakers)
                        {
                            var isSelected = speaker == _qwen3Speaker;
                            if (ImGui.Selectable(speaker, isSelected))
                            {
                                _qwen3Speaker = speaker;
                                changed = true;
                            }

                            if (isSelected)
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                    }

                    ImGui.EndCombo();
                }
            }

            ImGui.SameLine(0, 10);

            if (ImGui.Button("Refresh##Qwen3SpeakerRefresh", new Vector2(-1, 0)))
            {
                LoadAvailableQwen3SpeakersAsync();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Refresh available speakers list");
            }

            // Language
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Language:");
            ImGui.SameLine(120);
            ImGui.SetNextItemWidth(availWidth - 120);
            if (ImGui.InputText("##Qwen3Language", ref _qwen3Language, 256))
            {
                changed = true;
            }

            // Voice instruct
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Voice Instruct:");
            ImGui.SameLine(120);
            ImGui.SetNextItemWidth(availWidth - 120);
            if (
                ImGui.InputTextMultiline(
                    "##Qwen3Instruct",
                    ref _qwen3Instruct,
                    2048,
                    new Vector2(availWidth - 120, 60)
                )
            )
            {
                changed = true;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Describe the desired voice style (e.g., \"warm and breathy\"). Leave empty for default."
                );
            }
        }

        ImGui.EndGroup();

        // Talker sampling parameters
        ImGui.Spacing();
        ImGui.SeparatorText("Talker Sampling (Group 0)");
        ImGui.Spacing();

        ImGui.BeginGroup();
        {
            // Temperature
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Temperature:");
            ImGui.SameLine(160);
            ImGui.SetNextItemWidth(availWidth - 160);
            if (ImGui.SliderFloat("##Qwen3Temp", ref _qwen3Temperature, 0.1f, 2.0f, "%.2f"))
            {
                changed = true;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Controls randomness. Lower = more deterministic, higher = more varied."
                );
            }

            // Top K
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Top K:");
            ImGui.SameLine(160);
            ImGui.SetNextItemWidth(availWidth - 160);
            if (ImGui.SliderInt("##Qwen3TopK", ref _qwen3TopK, 1, 200))
            {
                changed = true;
            }

            // Top P
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Top P:");
            ImGui.SameLine(160);
            ImGui.SetNextItemWidth(availWidth - 160);
            if (ImGui.SliderFloat("##Qwen3TopP", ref _qwen3TopP, 0.1f, 1.0f, "%.2f"))
            {
                changed = true;
            }

            // Repetition Penalty
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Repetition Penalty:");
            ImGui.SameLine(160);
            ImGui.SetNextItemWidth(availWidth - 160);
            if (
                ImGui.SliderFloat(
                    "##Qwen3RepPenalty",
                    ref _qwen3RepetitionPenalty,
                    1.0f,
                    2.0f,
                    "%.2f"
                )
            )
            {
                changed = true;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Penalizes repeated tokens. 1.0 = no penalty.");
            }

            // Silence penalty toggle
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Silence Penalty:");
            ImGui.SameLine(160);
            if (ImGui.Checkbox("##Qwen3SilencePenalty", ref _qwen3SilencePenaltyEnabled))
            {
                changed = true;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Penalizes consecutive silent frames to prevent long silent tails.\n"
                        + "Custom heuristic, not in the official implementation."
                );
            }

            // Max New Tokens
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Max New Tokens:");
            ImGui.SameLine(160);
            ImGui.SetNextItemWidth(availWidth - 160);
            if (ImGui.SliderInt("##Qwen3MaxTokens", ref _qwen3MaxNewTokens, 256, 8192))
            {
                changed = true;
            }
        }

        ImGui.EndGroup();

        // Code Predictor sampling
        ImGui.Spacing();
        ImGui.SeparatorText("Code Predictor Sampling (Groups 1-15)");
        ImGui.Spacing();

        ImGui.BeginGroup();
        {
            // Greedy toggle
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Greedy (Argmax):");
            ImGui.SameLine(160);
            if (ImGui.Checkbox("##Qwen3CPGreedy", ref _qwen3CodePredictorGreedy))
            {
                changed = true;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "OFF (default): Uses temperature + top-K sampling (matches official repo).\n"
                        + "ON: Uses argmax/greedy decoding (matches some Rust implementations).\n"
                        + "Greedy may produce cleaner spectral output but less natural variation."
                );
            }

            // Show sampling params only when not greedy
            if (!_qwen3CodePredictorGreedy)
            {
                ImGui.TextColored(
                    new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                    "Uses Talker temperature and Top K settings above."
                );
            }
            else
            {
                ImGui.TextColored(
                    new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                    "Always picks the highest-probability token."
                );
            }
        }

        ImGui.EndGroup();

        if (changed)
        {
            UpdateConfiguration();
        }
    }

    private void RenderAdvancedSettings()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        // Section header
        ImGui.Spacing();
        ImGui.SeparatorText("Advanced TTS Settings");
        ImGui.Spacing();

        var sampleRateChanged = false;
        var phonemeChanged = false;

        // ImGui.BeginDisabled();
        ImGui.BeginGroup();
        {
            ImGui.BeginDisabled();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Sample Rate:");
            ImGui.SameLine(200);

            string[] sampleRates =
            [
                "16000 Hz (Low quality)",
                "24000 Hz (Standard quality)",
                "32000 Hz (Good quality)",
                "44100 Hz (High quality)",
                "48000 Hz (Studio quality)",
            ];
            int[] rateValues = [16000, 24000, 32000, 44100, 48000];

            var currentIdx = Array.IndexOf(rateValues, _sampleRate);
            if (currentIdx < 0)
            {
                currentIdx = 1; // Default to 24000 Hz
            }

            ImGui.SetNextItemWidth(availWidth - 200);

            if (ImGui.BeginCombo("##SampleRate", sampleRates[currentIdx]))
            {
                for (var i = 0; i < sampleRates.Length; i++)
                {
                    var isSelected = i == currentIdx;
                    if (ImGui.Selectable(sampleRates[i], isSelected))
                    {
                        _sampleRate = rateValues[i];
                        sampleRateChanged = true;
                    }

                    // Show additional info on hover
                    if (ImGui.IsItemHovered())
                    {
                        var tooltipText = i switch
                        {
                            0 => "Low quality, minimal resource usage",
                            1 => "Standard quality, recommended for most uses",
                            2 => "Good quality, balanced resource usage",
                            3 => "High quality, CD audio standard",
                            4 => "Studio quality, higher resource usage",
                            _ => "",
                        };

                        ImGui.SetTooltip(tooltipText);
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.EndDisabled();
        }

        ImGui.EndGroup();

        ImGui.BeginGroup();
        {
            ImGui.BeginDisabled();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Max Phoneme Length:");
            ImGui.SameLine(200);

            ImGui.SetNextItemWidth(120);
            phonemeChanged = ImGui.InputInt("##MaxPhonemeLength", ref _maxPhonemeLength);
            ImGui.EndDisabled();

            // Clamp value to valid range
            if (phonemeChanged)
            {
                var oldValue = _maxPhonemeLength;
                _maxPhonemeLength = Math.Clamp(_maxPhonemeLength, 1, 2048);

                if (oldValue != _maxPhonemeLength)
                {
                    ImGui.TextColored(
                        new Vector4(1.0f, 0.7f, 0.3f, 1.0f),
                        "Value clamped to valid range (1-2048)"
                    );
                }
            }

            // Helper text
            ImGui.Spacing();
            ImGui.TextWrapped(
                "This is already setup correctly to work with Kokoro. Shouldn't have to change!"
            );

            // === Paths & Resources Section ===
            ImGui.Spacing();
            ImGui.SeparatorText("Paths & Resources");
            ImGui.Spacing();

            ImGui.BeginGroup();
            {
                // Model directory
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Model Directory:");
                ImGui.SameLine(150);

                ImGui.SetNextItemWidth(availWidth - 240);
                var modelDirChanged = ImGui.InputText(
                    "##ModelDir",
                    ref _modelDir,
                    512,
                    ImGuiInputTextFlags.ElideLeft
                );

                ImGui.SameLine(0, 10);
                if (ImGui.Button("Browse##ModelDir", new Vector2(-1, 0)))
                {
                    var fileDialog = new OpenFolderDialog(_modelDir);
                    fileDialog.Show();
                    if (fileDialog.SelectedFolder != null)
                    {
                        _modelDir = fileDialog.SelectedFolder;
                        modelDirChanged = true;
                    }
                }

                // Espeak path
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Espeak Path:");
                ImGui.SameLine(150);

                ImGui.SetNextItemWidth(availWidth - 240);
                var espeakPathChanged = ImGui.InputText(
                    "##EspeakPath",
                    ref _espeakPath,
                    512,
                    ImGuiInputTextFlags.ElideLeft
                );

                ImGui.SameLine(0, 10);
                if (ImGui.Button("Browse##EspeakPath", new Vector2(-1, 0)))
                {
                    // In a real app, this would open a file browser dialog
                    Console.WriteLine("Open file browser for Espeak Path");
                }

                // Apply changes if needed
                if (modelDirChanged || espeakPathChanged)
                {
                    UpdateConfiguration();
                }
            }

            ImGui.EndGroup();
        }

        ImGui.EndGroup();

        if (sampleRateChanged || phonemeChanged)
        {
            UpdateConfiguration();
        }
    }

    #endregion
}
