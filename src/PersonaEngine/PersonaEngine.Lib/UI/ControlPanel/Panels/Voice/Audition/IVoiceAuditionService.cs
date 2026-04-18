namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;

public interface IVoiceAuditionService
{
    Task PreviewAsync(VoiceAuditionRequest request, CancellationToken ct = default);

    /// <summary>Stops the currently-playing preview, if any.</summary>
    Task StopAsync(CancellationToken ct = default);

    bool IsPreviewing { get; }

    /// <summary>
    ///     The <see cref="VoiceAuditionRequest.Id" /> of the currently-playing preview, or
    ///     <see langword="null" /> when idle. Used by preview buttons to decide their visual state
    ///     (playing / disabled / idle).
    /// </summary>
    string? ActivePreviewId { get; }

    event EventHandler<VoiceAuditionRequest>? PreviewStarted;
    event EventHandler? PreviewFinished;
}
