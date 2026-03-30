namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Manages configuration loading, saving, and access
/// </summary>
public interface IUiConfigurationManager
{
    event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;

    T GetConfiguration<T>(string sectionKey = null);

    void UpdateConfiguration<T>(T configuration, string? sectionKey = null);

    void SaveConfiguration();

    void ReloadConfiguration();
}
