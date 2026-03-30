namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Event arguments for configuration changes
/// </summary>
public class ConfigurationChangedEventArgs : EventArgs
{
    public enum ChangeType
    {
        Updated,

        Reloaded,

        Saved,
    }

    public ConfigurationChangedEventArgs(string sectionKey, ChangeType type)
    {
        SectionKey = sectionKey;
        Type = type;
    }

    public string SectionKey { get; }

    public ChangeType Type { get; }
}
