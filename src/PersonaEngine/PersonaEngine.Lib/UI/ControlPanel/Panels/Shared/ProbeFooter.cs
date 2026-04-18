namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

using Hexa.NET.ImGui;

/// <summary>
///     Shared footer row for endpoint-probe sections: left-aligned status text
///     ("Not tested yet" / "Testing…" / "Last tested: Nm ago") and a right-aligned
///     primary "Test connection" button with click-pulse feedback.
/// </summary>
/// <remarks>
///     The status text allocates zero strings on the steady-state hot path: the
///     "Last tested" prefix is cached against the underlying humanised "ago"
///     string (which is itself interned by <see cref="TimeFormat" />), so a new
///     string is only built when the bucket changes (once per second at the
///     finest granularity). Callers own the <paramref name="state" /> struct so
///     the cache persists across frames.
/// </remarks>
public static class ProbeFooter
{
    private const string NotTestedYet = "Not tested yet";

    private const string Testing = "Testing\u2026";

    private const string TestConnection = "Test connection";

    // PrimaryButtonWithFeedback uses FramePadding(16, 5); chrome ≈ text + 32.
    private const float PrimaryButtonChromeWidth = 32f;

    /// <summary>
    ///     Per-section render state for the footer. Caches the formatted
    ///     "Last tested: N ago" string across frames so the steady-state render
    ///     stays allocation-free. Mutated in place by <see cref="Render" /> via a
    ///     <see langword="ref" /> parameter.
    /// </summary>
    public struct State
    {
        /// <summary>
        ///     Monotonic click-pulse animation for the primary button. Owned by
        ///     the footer; callers should not mutate it directly.
        /// </summary>
        public OneShotAnimation Pulse;

        internal string? CachedAgoKey;

        internal string? CachedLastTestedText;
    }

    /// <summary>
    ///     Renders the footer row.
    /// </summary>
    /// <param name="state">Caller-owned render state (animations + cache).</param>
    /// <param name="lastProbeTime">
    ///     Timestamp of the most recent completed probe, or <see langword="null" />
    ///     when the endpoint has never been tested.
    /// </param>
    /// <param name="probeInFlight">
    ///     <see langword="true" /> while a probe is running — suppresses the
    ///     timestamp text, shows "Testing…" instead, and disables the button.
    /// </param>
    /// <param name="dt">Frame delta in seconds, for the pulse animation.</param>
    /// <param name="onTest">Invoked when the user clicks the Test button.</param>
    public static void Render(
        ref State state,
        DateTimeOffset? lastProbeTime,
        bool probeInFlight,
        float dt,
        Action onTest
    )
    {
        ArgumentNullException.ThrowIfNull(onTest);

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
        ImGui.TextUnformatted(FooterLeftText(ref state, lastProbeTime, probeInFlight));
        ImGui.PopStyleColor();

        ImGui.SameLine();
        var btnText = probeInFlight ? Testing : TestConnection;
        var btnW = ImGui.CalcTextSize(btnText).X + PrimaryButtonChromeWidth;
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, avail - btnW));

        ImGui.BeginDisabled(probeInFlight);
        if (ImGuiHelpers.PrimaryButtonWithFeedback(btnText, ref state.Pulse, dt))
        {
            onTest();
        }

        ImGui.EndDisabled();
    }

    private static string FooterLeftText(
        ref State state,
        DateTimeOffset? lastProbeTime,
        bool probeInFlight
    )
    {
        if (probeInFlight)
        {
            return Testing;
        }

        if (lastProbeTime is null)
        {
            return NotTestedYet;
        }

        var ago = TimeFormat.HumaniseAgo(DateTimeOffset.UtcNow - lastProbeTime.Value);

        // TimeFormat.HumaniseAgo returns cached, interned strings for every bucket
        // in the common range; a reference-equality check is enough to skip the
        // interpolation allocation on steady-state frames.
        if (ReferenceEquals(state.CachedAgoKey, ago) && state.CachedLastTestedText is not null)
        {
            return state.CachedLastTestedText;
        }

        state.CachedAgoKey = ago;
        state.CachedLastTestedText = $"Last tested: {ago}";
        return state.CachedLastTestedText;
    }
}
