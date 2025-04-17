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

namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Editor for TTS section configuration
/// </summary>
public class TtsConfigEditor : ConfigSectionEditorBase
{
    private readonly IAudioOutputAdapter _audioPlayer;

    private readonly IRVCVoiceProvider _rvcProvider;

    private readonly IUiThemeManager _themeManager;

    private readonly ITtsEngine _ttsEngine;

    private readonly IKokoroVoiceProvider _voiceProvider;

    private List<string> _availableRVCs = new();

    private List<string> _availableVoices = new();

    private TtsConfiguration _currentConfig;

    private RVCFilterOptions _currentRvcFilterOptions;

    private KokoroVoiceOptions _currentVoiceOptions;

    private string _defaultRVC;

    private string _defaultVoice;

    private string _espeakPath;

    private bool _isPlaying = false;

    private bool _loadingRvcs = false;

    private bool _loadingVoices = false;

    private int _maxPhonemeLength;

    private string _modelDir;

    private ActiveOperation? _playbackOperation = null;

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
        IEditorStateManager     stateManager,
        ITtsEngine              ttsEngine,
        IOutputAdapter          audioPlayer,
        IKokoroVoiceProvider    voiceProvider,
        IUiThemeManager         themeManager, IRVCVoiceProvider rvcProvider)
        : base(configManager, stateManager)
    {
        _ttsEngine     = ttsEngine;
        _audioPlayer   = (IAudioOutputAdapter)audioPlayer;
        _voiceProvider = voiceProvider;
        _themeManager  = themeManager;
        _rvcProvider   = rvcProvider;

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
    }

    public override void Render()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        // Main layout with tabs for different sections
        if ( ImGui.BeginTabBar("TtsConfigTabs") )
        {
            // Basic settings tab
            if ( ImGui.BeginTabItem("Basic Settings") )
            {
                RenderTestingSection();
                RenderBasicSettings();

                ImGui.EndTabItem();
            }

            // Advanced settings tab
            if ( ImGui.BeginTabItem("Advanced Settings") )
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
        if ( ImGui.Button("Reset", new Vector2(150, 0)) )
        {
            ResetToDefaults();
        }

        ImGui.SameLine(0, 10);

        if ( ImGui.IsItemHovered() )
        {
            ImGui.SetTooltip("Reset all TTS settings to default values");
        }

        if ( !StateManager.HasUnsavedChanges )
        {
            ImGui.BeginDisabled();
            ImGui.Button("Save", new Vector2(150, 0));
            ImGui.EndDisabled();
        }
        else if ( ImGui.Button("Save", new Vector2(150, 0)) )
        {
            SaveConfiguration();
        }
    }

    public override void Update(float deltaTime)
    {
        // Simplified update just for playback operation
        if ( _playbackOperation != null && _isPlaying )
        {
            _playbackOperation.Progress += deltaTime * 0.1f;
            if ( _playbackOperation.Progress > 0.99f )
            {
                _playbackOperation.Progress = 0.99f;
            }
        }
    }

    public override void OnConfigurationChanged(ConfigurationChangedEventArgs args)
    {
        base.OnConfigurationChanged(args);

        if ( args.Type == ConfigurationChangedEventArgs.ChangeType.Reloaded )
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
        _currentConfig           = ConfigManager.GetConfiguration<TtsConfiguration>("TTS");
        _currentVoiceOptions     = _currentConfig.Voice;
        _currentRvcFilterOptions = _currentConfig.Rvc;

        // Update local fields from configuration
        _modelDir          = _currentConfig.ModelDirectory;
        _espeakPath        = _currentConfig.EspeakPath;
        _speechRate        = _currentVoiceOptions.DefaultSpeed;
        _sampleRate        = _currentVoiceOptions.SampleRate;
        _trimSilence       = _currentVoiceOptions.TrimSilence;
        _useBritishEnglish = _currentVoiceOptions.UseBritishEnglish;
        _defaultVoice      = _currentVoiceOptions.DefaultVoice;
        _maxPhonemeLength  = _currentVoiceOptions.MaxPhonemeLength;

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
            var voices = await _voiceProvider.GetAvailableVoicesAsync();
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
            var voices = await _rvcProvider.GetAvailableVoicesAsync();
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

    private void UpdateConfiguration()
    {
        var updatedVoiceOptions = new KokoroVoiceOptions {
                                                             DefaultVoice      = _defaultVoice,
                                                             DefaultSpeed      = _speechRate,
                                                             SampleRate        = _sampleRate,
                                                             TrimSilence       = _trimSilence,
                                                             UseBritishEnglish = _useBritishEnglish,
                                                             MaxPhonemeLength  = _maxPhonemeLength
                                                         };

        var updatedRVCOptions = new RVCFilterOptions { DefaultVoice = _defaultRVC, Enabled = _rvcEnabled, HopSize = _rvcHopSize, F0UpKey = _rvcF0UpKey };

        var updatedConfig = new TtsConfiguration { ModelDirectory = _modelDir, EspeakPath = _espeakPath, Voice = updatedVoiceOptions, Rvc = updatedRVCOptions };

        _currentRvcFilterOptions = updatedRVCOptions;
        _currentVoiceOptions     = updatedVoiceOptions;
        _currentConfig           = updatedConfig;
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
        // Create default configuration
        var defaultVoiceOptions = new KokoroVoiceOptions();
        var defaultConfig       = new TtsConfiguration();

        // Update local state
        _currentVoiceOptions = defaultVoiceOptions;
        _currentConfig       = defaultConfig;

        // Update UI fields
        _modelDir          = defaultConfig.ModelDirectory;
        _espeakPath        = defaultConfig.EspeakPath;
        _speechRate        = defaultVoiceOptions.DefaultSpeed;
        _sampleRate        = defaultVoiceOptions.SampleRate;
        _trimSilence       = defaultVoiceOptions.TrimSilence;
        _useBritishEnglish = defaultVoiceOptions.UseBritishEnglish;
        _defaultVoice      = defaultVoiceOptions.DefaultVoice;
        _maxPhonemeLength  = defaultVoiceOptions.MaxPhonemeLength;

        // Update configuration
        ConfigManager.UpdateConfiguration(defaultConfig, "TTS");
        MarkAsChanged();
    }

    #endregion

    #region Playback Controls

    private async void StartPlayback()
    {
        if ( _isPlaying || string.IsNullOrWhiteSpace(_testText) )
        {
            return;
        }

        try
        {
            // Create a new playback operation
            _playbackOperation = new ActiveOperation("tts-playback", "Playing TTS");
            StateManager.RegisterActiveOperation(_playbackOperation);

            _isPlaying = true;

            var options = _currentVoiceOptions;

            var llmInput    = Channel.CreateUnbounded<LlmChunkEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
            var audioOutput = Channel.CreateUnbounded<TtsChunkEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
            var audioEvents = Channel.CreateUnbounded<IOutputEvent>(new UnboundedChannelOptions { SingleReader  = true, SingleWriter = true }); // Tts Started/Tts Ended - Audio Started/Audio Ended

            await llmInput.Writer.WriteAsync(new LlmChunkEvent(Guid.Empty, Guid.Empty, DateTimeOffset.UtcNow, _testText), _playbackOperation.CancellationSource.Token);
            llmInput.Writer.Complete();

            // Generate and play audio
            _ = _ttsEngine.SynthesizeStreamingAsync(
                                                    llmInput,
                                                    audioEvents,
                                                    Guid.Empty,
                                                    Guid.Empty,
                                                    options,
                                                    _playbackOperation.CancellationSource.Token
                                                   );

            await _audioPlayer.SendAsync(audioOutput, audioEvents, Guid.Empty, _playbackOperation.CancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("TTS playback cancelled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during TTS playback: {ex.Message}");
            _isPlaying = false;

            if ( _playbackOperation != null )
            {
                StateManager.ClearActiveOperation(_playbackOperation.Id);
                _playbackOperation = null;
            }
        }
    }

    private void StopPlayback()
    {
        if ( _playbackOperation != null )
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

        if ( _playbackOperation != null )
        {
            _playbackOperation.Progress = 0.0f;
        }
    }

    private void OnPlaybackCompleted(object sender, EventArgs args)
    {
        _isPlaying = false;

        if ( _playbackOperation != null )
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

            if ( ImGui.BeginCombo("##exampleLbl", "Select Example") )
            {
                string[] examples = { "Hello, world!", "The quick brown fox jumps over the lazy dog.", "Welcome to the text-to-speech system.", "How are you doing today?", "Today's date is March 3rd, 2025." };

                foreach ( var example in examples )
                {
                    var isSelected = _testText == example;
                    if ( ImGui.Selectable(example, ref isSelected) )
                    {
                        _testText = example;
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine(0, 15);

            var clearDisabled = string.IsNullOrEmpty(_testText);
            if ( clearDisabled )
            {
                ImGui.BeginDisabled();
            }

            if ( ImGui.Button("Clear", new Vector2(80, 0)) && !string.IsNullOrEmpty(_testText) )
            {
                _testText = "";
            }

            if ( clearDisabled )
            {
                ImGui.EndDisabled();
            }
        }

        ImGui.EndGroup();

        ImGui.SameLine(0, 10);

        // Playback controls in a styled frame
        {
            // Play/Stop button with color styling
            if ( _isPlaying )
            {
                if ( UiStyler.AnimatedButton("Stop", new Vector2(80, 0), _isPlaying) )
                {
                    StopPlayback();
                }

                ImGui.SameLine(0, 15);
                ImGui.ProgressBar(_playbackOperation?.Progress ?? 0, new Vector2(-1, 0), "Playing");
            }
            else
            {
                var disabled = string.IsNullOrWhiteSpace(_testText);
                if ( disabled )
                {
                    ImGui.BeginDisabled();
                }

                if ( UiStyler.AnimatedButton("Play", new Vector2(80, 0), _isPlaying) )
                {
                    StartPlayback();
                }

                if ( disabled )
                {
                    ImGui.EndDisabled();
                    if ( ImGui.IsItemHovered() )
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

        // === Voice Selection Section ===
        ImGui.Spacing();
        ImGui.SeparatorText("Voice Settings");
        ImGui.Spacing();

        ImGui.BeginGroup();
        {
            ImGui.SetNextItemWidth(availWidth - 120);

            if ( _loadingVoices )
            {
                ImGui.BeginDisabled();
                var loadingText = "Loading voices...";
                ImGui.InputText("##VoiceLoading", ref loadingText, 100, ImGuiInputTextFlags.ReadOnly);
                ImGui.EndDisabled();
            }
            else
            {
                if ( ImGui.BeginCombo("##VoiceSelector", string.IsNullOrEmpty(_defaultVoice) ? "<Select voice>" : _defaultVoice) )
                {
                    if ( _availableVoices.Count == 0 )
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No voices available");
                    }
                    else
                    {
                        foreach ( var voice in _availableVoices )
                        {
                            var isSelected = voice == _defaultVoice;
                            if ( ImGui.Selectable(voice, isSelected) )
                            {
                                _defaultVoice = voice;
                                UpdateConfiguration();
                            }

                            if ( isSelected )
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                    }

                    ImGui.EndCombo();
                }
            }

            ImGui.SameLine(0, 10);

            if ( ImGui.Button("Refresh##VoiceRefresh", new Vector2(-1, 0)) )
            {
                LoadAvailableVoicesAsync();
            }

            if ( ImGui.IsItemHovered() )
            {
                ImGui.SetTooltip("Refresh available voices list");
            }

            // Speech rate slider with better styling
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Speech Rate:");
            ImGui.SameLine(120);

            ImGui.SetNextItemWidth(availWidth - 120 - 120);
            var rateChanged = ImGui.SliderFloat("##SpeechRate", ref _speechRate, 0.5f, 2.0f, "%.2f");

            ImGui.SameLine(0, 10);

            if ( ImGui.Button("Reset##Rate", new Vector2(-1, 0)) )
            {
                _speechRate = 1.0f;
                rateChanged = true;
            }

            // Voice options with checkboxes in a consistent layout
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Options:");
            ImGui.SameLine(120);

            // Trim silence
            var trimChanged = ImGui.Checkbox("Trim Silence", ref _trimSilence);
            if ( ImGui.IsItemHovered() )
            {
                ImGui.SetTooltip("Remove silence from beginning and end of speech");
            }

            ImGui.SameLine(0, 50);

            // British English
            var britishChanged = ImGui.Checkbox("Use British English", ref _useBritishEnglish);
            if ( ImGui.IsItemHovered() )
            {
                ImGui.SetTooltip("Use British English pronunciation instead of American English");
            }

            // Apply changes if needed
            if ( rateChanged || trimChanged || britishChanged )
            {
                UpdateConfiguration();
            }
        }

        ImGui.EndGroup();

        // === RVC Selection Section ===
        ImGui.Spacing();
        ImGui.SeparatorText("RVC Settings");
        ImGui.Spacing();

        ImGui.BeginGroup();
        {
            var rvcConfigChanged = ImGui.Checkbox("Enable Voice Change", ref _rvcEnabled);

            ImGui.SetNextItemWidth(availWidth - 120);

            if ( !_rvcEnabled )
            {
                ImGui.BeginDisabled();
            }

            if ( _loadingVoices )
            {
                ImGui.BeginDisabled();
                var loadingText = "Loading voices...";
                ImGui.InputText("##RVCLoading", ref loadingText, 100, ImGuiInputTextFlags.ReadOnly);
                ImGui.EndDisabled();
            }
            else
            {
                if ( ImGui.BeginCombo("##RVCSelector", string.IsNullOrEmpty(_defaultRVC) ? "<Select voice>" : _defaultRVC) )
                {
                    if ( _availableRVCs.Count == 0 )
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No voices available");
                    }
                    else
                    {
                        foreach ( var voice in _availableRVCs )
                        {
                            var isSelected = voice == _defaultRVC;
                            if ( ImGui.Selectable(voice, isSelected) )
                            {
                                _defaultRVC = voice;
                                UpdateConfiguration();
                            }

                            if ( isSelected )
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                    }

                    ImGui.EndCombo();
                }
            }

            ImGui.SameLine(0, 10);

            if ( ImGui.Button("Refresh##RefreshRVC", new Vector2(-1, 0)) )
            {
                LoadAvailableRVCAsync();
            }

            if ( ImGui.IsItemHovered() )
            {
                ImGui.SetTooltip("Refresh available voices list");
            }

            if ( ImGui.BeginTable("RVCProps", 4, ImGuiTableFlags.SizingFixedFit) )
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
                if ( fontSizeChanged )
                {
                    _rvcHopSize      = Math.Clamp(_rvcHopSize, 8, 256);
                    rvcConfigChanged = true;
                }

                // F0 Key
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Pitch");
                ImGui.TableNextColumn();
                var pitchChanged = ImGui.InputInt("##Pitch", ref _rvcF0UpKey, 1);
                if ( pitchChanged )
                {
                    _rvcF0UpKey      = Math.Clamp(_rvcF0UpKey, -20, 20);
                    rvcConfigChanged = true;
                }

                ImGui.EndTable();
            }

            if ( !_rvcEnabled )
            {
                ImGui.EndDisabled();
            }

            if ( rvcConfigChanged )
            {
                UpdateConfiguration();
            }
        }

        ImGui.EndGroup();
    }

    private void RenderAdvancedSettings()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        // Section header
        ImGui.Spacing();
        ImGui.SeparatorText("Advanced TTS Settings");
        ImGui.Spacing();

        var sampleRateChanged = false;
        var phonemeChanged    = false;

        // ImGui.BeginDisabled();
        ImGui.BeginGroup();
        {
            ImGui.BeginDisabled();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Sample Rate:");
            ImGui.SameLine(200);

            string[] sampleRates = ["16000 Hz (Low quality)", "24000 Hz (Standard quality)", "32000 Hz (Good quality)", "44100 Hz (High quality)", "48000 Hz (Studio quality)"];
            int[]    rateValues  = [16000, 24000, 32000, 44100, 48000];

            var currentIdx = Array.IndexOf(rateValues, _sampleRate);
            if ( currentIdx < 0 )
            {
                currentIdx = 1; // Default to 24000 Hz
            }

            ImGui.SetNextItemWidth(availWidth - 200);

            if ( ImGui.BeginCombo("##SampleRate", sampleRates[currentIdx]) )
            {
                for ( var i = 0; i < sampleRates.Length; i++ )
                {
                    var isSelected = i == currentIdx;
                    if ( ImGui.Selectable(sampleRates[i], isSelected) )
                    {
                        _sampleRate       = rateValues[i];
                        sampleRateChanged = true;
                    }

                    // Show additional info on hover
                    if ( ImGui.IsItemHovered() )
                    {
                        var tooltipText = i switch {
                            0 => "Low quality, minimal resource usage",
                            1 => "Standard quality, recommended for most uses",
                            2 => "Good quality, balanced resource usage",
                            3 => "High quality, CD audio standard",
                            4 => "Studio quality, higher resource usage",
                            _ => ""
                        };

                        ImGui.SetTooltip(tooltipText);
                    }

                    if ( isSelected )
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
            if ( phonemeChanged )
            {
                var oldValue = _maxPhonemeLength;
                _maxPhonemeLength = Math.Clamp(_maxPhonemeLength, 1, 2048);

                if ( oldValue != _maxPhonemeLength )
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.3f, 1.0f),
                                      "Value clamped to valid range (1-2048)");
                }
            }

            // Helper text
            ImGui.Spacing();
            ImGui.TextWrapped("This is already setup correctly to work with Kokoro. Shouldn't have to change!");

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
                var modelDirChanged = ImGui.InputText("##ModelDir", ref _modelDir, 512, ImGuiInputTextFlags.ElideLeft);

                ImGui.SameLine(0, 10);
                if ( ImGui.Button("Browse##ModelDir", new Vector2(-1, 0)) )
                {
                    var fileDialog = new OpenFolderDialog(_modelDir);
                    fileDialog.Show();
                    if ( fileDialog.SelectedFolder != null )
                    {
                        _modelDir       = fileDialog.SelectedFolder;
                        modelDirChanged = true;
                    }
                }

                // Espeak path
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Espeak Path:");
                ImGui.SameLine(150);

                ImGui.SetNextItemWidth(availWidth - 240);
                var espeakPathChanged = ImGui.InputText("##EspeakPath", ref _espeakPath, 512, ImGuiInputTextFlags.ElideLeft);

                ImGui.SameLine(0, 10);
                if ( ImGui.Button("Browse##EspeakPath", new Vector2(-1, 0)) )
                {
                    // In a real app, this would open a file browser dialog
                    Console.WriteLine("Open file browser for Espeak Path");
                }

                // Apply changes if needed
                if ( modelDirChanged || espeakPathChanged )
                {
                    UpdateConfiguration();
                }
            }

            ImGui.EndGroup();
        }

        ImGui.EndGroup();

        if ( sampleRateChanged || phonemeChanged )
        {
            UpdateConfiguration();
        }
    }

    #endregion
}