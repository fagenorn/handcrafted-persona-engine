namespace PersonaEngine.Lib.UI.ControlPanel.Threading;

/// <summary>
///     Marshals work from off-UI callbacks (probes, IOptionsMonitor.OnChange, orchestrator
///     events) onto the render thread. Consumers <c>Post</c> an <see cref="Action" /> from
///     any thread; the UI component owning the render loop calls <see cref="DrainPending" />
///     at the top of its frame to execute queued work inline.
/// </summary>
public interface IUiThreadDispatcher
{
    /// <summary>
    ///     Enqueue <paramref name="work" /> to run on the next UI frame. Thread-safe.
    ///     <para>
    ///         The implementation bounds its internal queue at an implementation-defined
    ///         maximum depth. When <see cref="Post" /> is called while the queue is at
    ///         that cap, the <em>oldest</em> pending action is dropped to make room for
    ///         the new one. Drops are counted and surfaced via the implementation's
    ///         diagnostics; a warning is logged at intervals so a sustained drop storm
    ///         does not flood the log. This bounds memory under producer-side cascades
    ///         (e.g. probe storms, TTS error loops) at the cost of losing the oldest
    ///         deferred work — callers must not assume posted work always runs.
    ///     </para>
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="work"/> is <see langword="null"/>.</exception>
    void Post(Action work);

    /// <summary>
    ///     Drain up to an implementation-defined per-frame cap of queued actions, running
    ///     each inline on the calling (UI) thread. Must be invoked from the UI thread.
    ///     Exceptions from individual actions are logged and swallowed.
    ///     Actions posted during a drain may be picked up in the same drain if the
    ///     per-frame cap has not yet been reached; otherwise they are deferred to
    ///     the next drain.
    /// </summary>
    void DrainPending();
}
