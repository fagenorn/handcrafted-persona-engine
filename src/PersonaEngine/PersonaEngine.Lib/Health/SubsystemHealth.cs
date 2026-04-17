namespace PersonaEngine.Lib.Health;

/// <summary>
///     Coarse health buckets surfaced by every subsystem probe. The Dashboard
///     health cards map these to dot colour + label tone.
/// </summary>
public enum SubsystemHealth
{
    Unknown,
    Healthy,
    Degraded,
    Failed,
    Disabled,
}

/// <summary>
///     Latest observation from a subsystem probe — the compact payload the
///     Dashboard health cards consume.
/// </summary>
/// <param name="Health">Coarse bucket driving dot colour + label tone.</param>
/// <param name="Label">Short one-line status string shown on the card (e.g. "Healthy", "No API key").</param>
/// <param name="Detail">Optional longer explanation for a tooltip or expanded view; <c>null</c> when nothing to add.</param>
public readonly record struct SubsystemStatus(SubsystemHealth Health, string Label, string? Detail);
