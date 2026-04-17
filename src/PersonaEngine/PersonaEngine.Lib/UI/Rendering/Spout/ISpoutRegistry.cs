namespace PersonaEngine.Lib.UI.Rendering.Spout;

/// <summary>
///     Minimal read-only surface the Dashboard health layer needs to compare
///     configured Spout senders against how many are actually publishing frames.
///     The concrete <see cref="SpoutRegistry" /> owns one
///     <see cref="SpoutManager" /> per configured output; this interface exposes
///     only the counts and a change notification so probes can stay ignorant of
///     the underlying OpenGL/FBO details.
/// </summary>
/// <remarks>
///     <see cref="SendersChanged" /> fires on sender activation/deactivation
///     transitions only. Subscribers must read <see cref="ConfiguredSenderCount" />
///     and <see cref="ActiveSenderCount" /> once on attach to bootstrap their view.
/// </remarks>
public interface ISpoutRegistry
{
    /// <summary>
    ///     Total number of Spout outputs declared in configuration — i.e. how
    ///     many senders the app intends to publish when fully healthy.
    /// </summary>
    int ConfiguredSenderCount { get; }

    /// <summary>
    ///     Number of configured senders that currently hold a live Spout sender
    ///     handle. Lower than <see cref="ConfiguredSenderCount" /> when one or
    ///     more senders failed to create or were toggled off.
    /// </summary>
    int ActiveSenderCount { get; }

    /// <summary>
    ///     Raised when any underlying sender transitions between active and
    ///     inactive. Consumers should re-read the two count properties in the
    ///     handler to recompute status.
    /// </summary>
    event Action? SendersChanged;
}
