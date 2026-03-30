namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Exception thrown when saving configuration fails
/// </summary>
public class ConfigurationSaveException : Exception
{
    public ConfigurationSaveException(string message)
        : base(message) { }

    public ConfigurationSaveException(string message, Exception innerException)
        : base(message, innerException) { }
}
