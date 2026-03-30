namespace PersonaEngine.Lib.Configuration;

public record MicrophoneConfiguration
{
    /// <summary>
    ///     The friendly name of the desired input device.
    ///     If null, empty, or whitespace, the default device (DeviceNumber 0) will be used.
    /// </summary>
    public string? DeviceName { get; init; } = null;
}
