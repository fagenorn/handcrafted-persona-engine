using PersonaEngine.Lib.Assets.Manifest;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>Outcome of a bootstrap run, surfaced to App so it can decide whether to launch the UI.</summary>
public sealed record BootstrapResult
{
    public required bool Success { get; init; }

    /// <summary>Profile that ended up active (either picked, or read from lock).</summary>
    public required ProfileTier ActiveProfile { get; init; }

    /// <summary>True if the bootstrapper actually performed downloads/extractions during this run.</summary>
    public required bool ChangesApplied { get; init; }

    /// <summary>Non-fatal warnings (e.g. one optional asset failed but core launch is fine).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Fatal error message if Success is false.</summary>
    public string? ErrorMessage { get; init; }
}
