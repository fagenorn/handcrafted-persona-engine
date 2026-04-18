namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Tracks the maximum height of a group of auto-sized items across frames so they can
///     be rendered at a uniform height. On the first frame, items auto-size and the tracker
///     records the tallest one. On subsequent frames, <see cref="Height" /> returns that
///     value so callers can pass it as an explicit height (e.g., to <see cref="Ui.Card" />).
///     <para>
///         Call <see cref="Track" /> after each item is rendered (measures <c>GetItemRectSize().Y</c>).
///         Call <see cref="EndFrame" /> once at the end of the group to latch the max for next frame.
///     </para>
/// </summary>
public sealed class UniformHeightTracker
{
    private float _height;
    private float _nextHeight;

    /// <summary>
    ///     The uniform height to use this frame. Returns 0 on the first frame (meaning
    ///     "auto-size") so the tracker can measure natural heights before enforcing uniformity.
    /// </summary>
    public float Height => _height;

    /// <summary>
    ///     Records the rendered height of an item. Call after each card/tile in the group.
    ///     Pass the height captured inside the item (e.g., <c>ImGui.GetWindowSize().Y</c>
    ///     before <c>EndChild</c>) for reliable measurement.
    /// </summary>
    public void Track(float renderedHeight)
    {
        if (renderedHeight > _nextHeight)
            _nextHeight = renderedHeight;
    }

    /// <summary>
    ///     Latches the max height from this frame for use on the next frame, then resets
    ///     the accumulator. Call once after all items in the group have been rendered.
    /// </summary>
    public void EndFrame()
    {
        _height = _nextHeight;
        _nextHeight = 0f;
    }
}
