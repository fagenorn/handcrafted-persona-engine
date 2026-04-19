using PersonaEngine.Lib.Bootstrapper.Manifest;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>Outcome of a bootstrap run, surfaced to App so it can decide whether to launch the UI.</summary>
public sealed record BootstrapResult
{
    public required bool Success { get; init; }
    public required ProfileTier ActiveProfile { get; init; }
    public required bool ChangesApplied { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string? ErrorMessage { get; init; }
}
