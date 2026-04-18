namespace PersonaEngine.Lib.TTS.RVC;

/// <summary>
///     Per-call override for ad-hoc RVC processing (e.g., voice auditioning). Lets the
///     audition pipeline swap voice and pitch without touching the ambient
///     <see cref="RVCFilterOptions" /> used by the live conversation.
/// </summary>
public sealed record RvcOverride
{
    public string? Voice { get; init; }
    public int? F0UpKey { get; init; }
}
