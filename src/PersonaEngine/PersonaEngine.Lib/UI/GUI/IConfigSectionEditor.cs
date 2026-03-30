namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Interface for configuration section editors
/// </summary>
public interface IConfigSectionEditor
{
    string SectionKey { get; }

    string DisplayName { get; }

    bool HasUnsavedChanges { get; }

    void Initialize();

    void Render();

    void RenderMenuItems();

    void Update(float deltaTime);

    void OnConfigurationChanged(ConfigurationChangedEventArgs args);
}
