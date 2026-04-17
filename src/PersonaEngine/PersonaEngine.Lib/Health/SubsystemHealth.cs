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

/// <summary>Latest observation from a subsystem probe.</summary>
public readonly record struct SubsystemStatus(SubsystemHealth Health, string Label, string? Detail);
