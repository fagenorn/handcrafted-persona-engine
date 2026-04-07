using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Implements centralized configuration management
/// </summary>
public class UiConfigurationManager : IUiConfigurationManager
{
    private readonly string _configFilePath;

    private readonly IOptionsMonitor<AvatarAppConfig> _configMonitor;

    private AvatarAppConfig _currentConfig;

    public UiConfigurationManager(
        IOptionsMonitor<AvatarAppConfig> configMonitor,
        string configFilePath = "appsettings.json"
    )
    {
        _configMonitor = configMonitor ?? throw new ArgumentNullException(nameof(configMonitor));
        _configFilePath = configFilePath;
        _currentConfig = _configMonitor.CurrentValue;

        // Subscribe to configuration changes
        _configMonitor.OnChange(config =>
        {
            _currentConfig = config;
            OnConfigurationChanged(null, ConfigurationChangedEventArgs.ChangeType.Reloaded);
        });
    }

    public event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;

    public T GetConfiguration<T>(string sectionKey = null)
    {
        if (sectionKey == null)
        {
            if (typeof(T) == typeof(AvatarAppConfig))
            {
                return (T)(object)_currentConfig;
            }

            throw new ArgumentException(
                $"Invalid configuration type {typeof(T).Name} without section key"
            );
        }

        return sectionKey switch
        {
            "TTS" => (T)(object)_currentConfig.Tts,
            "Kokoro" => (T)(object)_currentConfig.Tts.Kokoro,
            "RouletteWheel" => (T)(object)_currentConfig.RouletteWheel,
            "Microphone" => (T)(object)_currentConfig.Microphone,
            _ => throw new ArgumentException($"Unknown section key: {sectionKey}"),
        };
    }

    public void UpdateConfiguration<T>(T configuration, string? sectionKey = null)
    {
        if (sectionKey == null)
        {
            if (configuration is AvatarAppConfig appConfig)
            {
                _currentConfig = appConfig;
                OnConfigurationChanged(
                    sectionKey,
                    ConfigurationChangedEventArgs.ChangeType.Updated
                );

                return;
            }

            throw new ArgumentException(
                $"Invalid configuration type {typeof(T).Name} without section key"
            );
        }

        switch (sectionKey)
        {
            case "TTS":
                if (configuration is TtsConfiguration ttsConfig)
                {
                    _currentConfig = _currentConfig with { Tts = ttsConfig };
                    OnConfigurationChanged(
                        sectionKey,
                        ConfigurationChangedEventArgs.ChangeType.Updated
                    );
                }
                else
                {
                    throw new ArgumentException(
                        $"Invalid configuration type {typeof(T).Name} for section {sectionKey}"
                    );
                }

                break;

            case "Kokoro":
                if (configuration is KokoroVoiceOptions voiceOptions)
                {
                    var tts = _currentConfig.Tts;
                    _currentConfig = _currentConfig with
                    {
                        Tts = tts with { Kokoro = voiceOptions },
                    };
                    OnConfigurationChanged(
                        sectionKey,
                        ConfigurationChangedEventArgs.ChangeType.Updated
                    );
                }
                else
                {
                    throw new ArgumentException(
                        $"Invalid configuration type {typeof(T).Name} for section {sectionKey}"
                    );
                }

                break;

            case "Roulette":
                if (configuration is RouletteWheelOptions rouletteWheelOptions)
                {
                    _currentConfig = _currentConfig with { RouletteWheel = rouletteWheelOptions };
                    OnConfigurationChanged(
                        sectionKey,
                        ConfigurationChangedEventArgs.ChangeType.Updated
                    );
                }
                else
                {
                    throw new ArgumentException(
                        $"Invalid configuration type {typeof(T).Name} for section {sectionKey}"
                    );
                }

                break;
            case "Microphone":
                if (configuration is MicrophoneConfiguration microphoneConfig)
                {
                    _currentConfig = _currentConfig with { Microphone = microphoneConfig };
                    OnConfigurationChanged(
                        sectionKey,
                        ConfigurationChangedEventArgs.ChangeType.Updated
                    );
                }
                else
                {
                    throw new ArgumentException(
                        $"Invalid configuration type {typeof(T).Name} for section {sectionKey}"
                    );
                }

                break;
            default:
                throw new ArgumentException($"Unknown section key: {sectionKey}");
        }
    }

    public void SaveConfiguration()
    {
        // Save the configuration to the JSON file
        try
        {
            var jsonString = JsonSerializer.Serialize(
                new Dictionary<string, object> { { "Config", _currentConfig } },
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                }
            );

            File.WriteAllText(_configFilePath, jsonString);
            OnConfigurationChanged(null, ConfigurationChangedEventArgs.ChangeType.Saved);
        }
        catch (Exception ex)
        {
            // Convert to a more specific exception type with details
            throw new ConfigurationSaveException(
                $"Failed to save configuration to {_configFilePath}",
                ex
            );
        }
    }

    public void ReloadConfiguration()
    {
        // Load the latest configuration from options monitor
        _currentConfig = _configMonitor.CurrentValue;
        OnConfigurationChanged(null, ConfigurationChangedEventArgs.ChangeType.Reloaded);
    }

    private void OnConfigurationChanged(
        string sectionKey,
        ConfigurationChangedEventArgs.ChangeType type
    )
    {
        ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs(sectionKey, type));
    }
}
