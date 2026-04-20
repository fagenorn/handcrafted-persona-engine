namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Models;

public enum VoiceEngine
{
    Kokoro,
    Qwen3,
    Rvc,
}

public enum VoiceGender
{
    Female,
    Male,
    Neutral,
}

/// <summary>
///     Metadata about a voice for UI display and filtering. Loaded from the embedded
///     <c>voice_metadata.json</c> resource by <see cref="VoiceMetadataCatalog" />.
/// </summary>
public sealed record VoiceDescriptor
{
    public required VoiceEngine Engine { get; init; }
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public VoiceGender? Gender { get; init; }
    public string? Description { get; init; }

    /// <summary>Fallback descriptor for voices not present in the metadata sidecar.</summary>
    public static VoiceDescriptor Fallback(VoiceEngine engine, string id) =>
        new()
        {
            Engine = engine,
            Id = id,
            DisplayName = id,
        };
}
