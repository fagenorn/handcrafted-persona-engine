using System.Diagnostics;
using System.Numerics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Editor for Microphone section configuration.
///     Allows selection of the audio input device.
/// </summary>
public class MicrophoneConfigEditor : ConfigSectionEditorBase
{
    private const string DefaultDeviceDisplayName = "(Default Device)"; // Display name for null/empty device

    // --- Dependencies ---
    private readonly IMicrophone _microphone; // Dependency to get device list

    private List<string> _availableDevices = new();

    // --- State ---
    private MicrophoneConfiguration _currentConfig;

    private bool _loadingDevices = false;

    private string? _selectedDeviceName; // Can be null for default device

    /// <summary>
    ///     Initializes a new instance of the <see cref="MicrophoneConfigEditor" /> class.
    /// </summary>
    /// <param name="configManager">The configuration manager.</param>
    /// <param name="stateManager">The editor state manager.</param>
    /// <param name="microphone">The microphone service.</param>
    public MicrophoneConfigEditor(
        IUiConfigurationManager configManager,
        IEditorStateManager stateManager,
        IMicrophone microphone
    )
        : base(configManager, stateManager)
    {
        _microphone = microphone ?? throw new ArgumentNullException(nameof(microphone));

        // Load initial configuration
        LoadConfiguration();
    }

    // --- ConfigSectionEditorBase Implementation ---

    /// <summary>
    ///     Gets the key for the configuration section managed by this editor.
    /// </summary>
    public override string SectionKey => "Microphone"; // Or your specific key

    /// <summary>
    ///     Gets the display name for this editor section.
    /// </summary>
    public override string DisplayName => "Microphone Settings";

    /// <summary>
    ///     Initializes the editor, loading necessary data like available devices.
    /// </summary>
    public override void Initialize()
    {
        LoadAvailableDevices(); // Load devices on initialization
    }

    /// <summary>
    ///     Renders the ImGui UI for the microphone configuration.
    /// </summary>
    public override void Render()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        // --- Device Selection Section ---
        ImGui.Spacing();
        ImGui.SeparatorText("Input Device Selection");
        ImGui.Spacing();

        ImGui.BeginGroup(); // Group controls for alignment
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Input Device:");
            ImGui.SameLine(120); // Adjust spacing as needed

            // Combo box for device selection
            ImGui.SetNextItemWidth(availWidth - 120 - 100); // Width calculation: Available - Label - Refresh Button

            var currentSelectionDisplay = string.IsNullOrEmpty(_selectedDeviceName)
                ? DefaultDeviceDisplayName
                : _selectedDeviceName;

            if (_loadingDevices)
            {
                // Show loading state
                ImGui.BeginDisabled();
                var loadingText = "Loading devices...";
                // Use InputText as a visual placeholder during loading
                ImGui.InputText(
                    "##DeviceLoading",
                    ref loadingText,
                    100,
                    ImGuiInputTextFlags.ReadOnly
                );
                ImGui.EndDisabled();
            }
            else
            {
                // Actual combo box
                if (ImGui.BeginCombo("##DeviceSelector", currentSelectionDisplay))
                {
                    // Add "(Default Device)" option first
                    var isDefaultSelected = string.IsNullOrEmpty(_selectedDeviceName);
                    if (ImGui.Selectable(DefaultDeviceDisplayName, isDefaultSelected))
                    {
                        if (_selectedDeviceName != null) // Check if changed
                        {
                            _selectedDeviceName = null;
                            UpdateConfiguration();
                        }
                    }

                    if (isDefaultSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }

                    // Add actual devices if available
                    if (_availableDevices.Count == 0 && !_loadingDevices)
                    {
                        ImGui.TextColored(
                            new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                            "No input devices found."
                        );
                    }
                    else
                    {
                        foreach (var device in _availableDevices)
                        {
                            var isSelected = device == _selectedDeviceName;
                            if (ImGui.Selectable(device, isSelected))
                            {
                                if (_selectedDeviceName != device) // Check if changed
                                {
                                    _selectedDeviceName = device;
                                    UpdateConfiguration();
                                }
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

            // Refresh Button
            ImGui.SameLine(0, 10); // Add spacing before button
            if (ImGui.Button("Refresh##Dev", new Vector2(80, 0))) // Unique ID for button
            {
                LoadAvailableDevices();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Refresh the list of available input devices.");
            }
        }

        ImGui.EndGroup(); // End device selection group

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Action Buttons ---
        // Center the buttons roughly
        float buttonWidth = 150;
        var totalButtonWidth = buttonWidth * 2 + 10; // Two buttons + spacing
        var initialPadding = (availWidth - totalButtonWidth) * 0.5f;
        if (initialPadding < 0)
        {
            initialPadding = 0;
        }

        ImGui.SetCursorPosX(initialPadding);

        // Reset Button
        if (ImGui.Button("Reset", new Vector2(buttonWidth, 0)))
        {
            ResetToDefaults();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Reset microphone settings to default values.");
        }

        ImGui.SameLine(0, 10); // Spacing between buttons

        // Save Button (Disabled if no changes)
        var hasChanges = StateManager.HasUnsavedChanges; // Check unsaved state
        if (!hasChanges)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Save", new Vector2(buttonWidth, 0)))
        {
            SaveConfiguration();
        }

        if (ImGui.IsItemHovered() && hasChanges)
        {
            ImGui.SetTooltip("Save the current microphone settings.");
        }

        if (!hasChanges)
        {
            ImGui.EndDisabled();
        }
    }

    /// <summary>
    ///     Updates the editor state (currently unused for this simple editor).
    /// </summary>
    /// <param name="deltaTime">Time elapsed since the last frame.</param>
    public override void Update(float deltaTime)
    {
        // No per-frame update logic needed for this editor yet
    }

    /// <summary>
    ///     Handles configuration changes, reloading if necessary.
    /// </summary>
    /// <param name="args">Event arguments containing change details.</param>
    public override void OnConfigurationChanged(ConfigurationChangedEventArgs args)
    {
        base.OnConfigurationChanged(args); // Call base implementation

        // Reload configuration if the source indicates a full reload
        if (args.Type == ConfigurationChangedEventArgs.ChangeType.Reloaded)
        {
            LoadConfiguration();
            // Optionally reload devices if the config might affect them,
            // though usually device list is independent of config.
            // LoadAvailableDevices();
        }
    }

    /// <summary>
    ///     Disposes resources used by the editor (currently none specific).
    /// </summary>
    public override void Dispose()
    {
        // Unsubscribe from events if any were added
        base.Dispose(); // Call base implementation
    }

    // --- Configuration Management ---

    /// <summary>
    ///     Loads the microphone configuration from the configuration manager.
    /// </summary>
    private void LoadConfiguration()
    {
        _currentConfig =
            ConfigManager.GetConfiguration<MicrophoneConfiguration>(SectionKey)
            ?? new MicrophoneConfiguration(); // Get or create default

        // Update local state from loaded config
        _selectedDeviceName = _currentConfig.DeviceName;

        // Ensure the UI reflects the loaded state without marking as changed initially
        MarkAsSaved(); // Reset change tracking after loading
    }

    /// <summary>
    ///     Fetches the list of available microphone devices.
    /// </summary>
    private void LoadAvailableDevices()
    {
        if (_loadingDevices)
        {
            return; // Prevent concurrent loading
        }

        try
        {
            _loadingDevices = true;
            // Consider adding an ActiveOperation to StateManager if this takes time
            // var operation = new ActiveOperation("load-mic-devices", "Loading Microphones");
            // StateManager.RegisterActiveOperation(operation);

            // Get devices using the injected service
            _availableDevices = _microphone.GetAvailableDevices().ToList();

            // Ensure the currently selected device still exists
            if (
                !string.IsNullOrEmpty(_selectedDeviceName)
                && !_availableDevices.Contains(_selectedDeviceName)
            )
            {
                Debug.WriteLine(
                    $"Warning: Configured microphone '{_selectedDeviceName}' not found. Reverting to default."
                );
                // Optionally notify the user here
                _selectedDeviceName = null; // Revert to default
                UpdateConfiguration(); // Update the config state
            }

            // StateManager.ClearActiveOperation(operation.Id);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading microphone devices: {ex.Message}");
            _availableDevices = new List<string>(); // Clear list on error
            // Optionally show an error message to the user via ImGui or logging
        }
        finally
        {
            _loadingDevices = false;
        }
    }

    /// <summary>
    ///     Updates the configuration object and notifies the manager.
    /// </summary>
    private void UpdateConfiguration()
    {
        // Create the updated configuration record
        var updatedConfig = _currentConfig with // Use record 'with' expression
        {
            DeviceName = _selectedDeviceName,
        };

        // Check if the configuration actually changed before updating
        if (!_currentConfig.Equals(updatedConfig))
        {
            _currentConfig = updatedConfig;
            ConfigManager.UpdateConfiguration(_currentConfig, SectionKey);
            MarkAsChanged(); // Mark that there are unsaved changes
        }
    }

    /// <summary>
    ///     Saves the current configuration changes.
    /// </summary>
    private void SaveConfiguration()
    {
        ConfigManager.SaveConfiguration();
        MarkAsSaved(); // Mark changes as saved
    }

    /// <summary>
    ///     Resets the microphone configuration to default values.
    /// </summary>
    private void ResetToDefaults()
    {
        var defaultConfig = new MicrophoneConfiguration(); // Create default config

        // Update local state
        _selectedDeviceName = defaultConfig.DeviceName; // Should be null

        // Update configuration only if it differs from current
        if (!_currentConfig.Equals(defaultConfig))
        {
            _currentConfig = defaultConfig;
            ConfigManager.UpdateConfiguration(_currentConfig, SectionKey);
            MarkAsChanged(); // Mark changes needing save
        }
        else
        {
            // If already default, just ensure saved state is correct
            MarkAsSaved();
        }
    }
}
