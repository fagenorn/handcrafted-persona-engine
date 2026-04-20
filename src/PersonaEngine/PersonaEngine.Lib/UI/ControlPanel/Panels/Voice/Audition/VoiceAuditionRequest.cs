namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;

public sealed record VoiceAuditionRequest
{
    /// <summary>
    ///     Stable identifier for the button that triggered this preview (e.g., "mode_Clear",
    ///     "tile_kokoro_af_heart"). Used by the service to expose
    ///     <see cref="IVoiceAuditionService.ActivePreviewId" /> so callers can determine
    ///     which button is currently active.
    /// </summary>
    public required string Id { get; init; }

    public required string Engine { get; init; }
    public required string Voice { get; init; }
    public float Speed { get; init; } = 1f;
    public float? Expressiveness { get; init; }
    public bool RvcEnabled { get; init; }
    public string? RvcVoice { get; init; }
    public int RvcPitchShift { get; init; }
}
