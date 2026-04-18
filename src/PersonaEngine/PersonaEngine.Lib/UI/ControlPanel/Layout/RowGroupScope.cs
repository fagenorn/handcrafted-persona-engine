using System.Collections.Concurrent;
using System.Numerics;
using Hexa.NET.ImGui;

namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Resolves a sequence of row sizes so that <see cref="Sz.Fill"/> rows correctly
///     account for their siblings. Supports three row modes:
///     <list type="bullet">
///         <item><see cref="Sz.Fixed"/> — exact pixel height.</item>
///         <item>
///             <see cref="Sz.Auto"/> — row opens an <c>AutoResizeY</c> child so it
///             grows to fit its content. The measured height is captured on dispose
///             and cached per group id so Fill rows can account for it.
///         </item>
///         <item>
///             <see cref="Sz.Fill"/> — claims whatever space remains after Fixed + Auto
///             rows have taken theirs. Fill height is resolved lazily when the row
///             opens, using measured heights for autos that have already rendered
///             and cached (last-frame) heights for autos still to come.
///         </item>
///     </list>
///     Call <see cref="Next"/> for each row in order.
/// </summary>
public ref struct RowGroupScope
{
    /// <summary>
    ///     Cache of per-group auto-row heights, keyed by the caller-supplied group id.
    ///     Updated on <see cref="Dispose"/> so the next frame has stable reservations
    ///     for Fill-row math. Static so each group carries its own history across frames.
    /// </summary>
    private static readonly ConcurrentDictionary<string, float[]> AutoHeightCache = new();

    private readonly string _cacheKey;
    private readonly Sz[] _sizes;

    // Live per-index auto heights. Seeded from the cache (or zeros on first frame);
    // overwritten with measured values as each auto row closes. Used every time a Fill
    // row opens so Fill's share always reflects the best information available.
    private readonly float[] _autoHeights;

    private readonly float _parentWidth;
    private readonly float _parentAvail;
    private readonly float _gap;
    private readonly float _totalFixed;
    private readonly float _totalFillWeight;
    private readonly Vector2 _parentSpacing;
    private int _nextIndex;
    private bool _disposed;

    // Track the row that was opened most recently so we can capture its rendered
    // height (for auto rows) after it disposes. ImGui's "last item" is the ended
    // child of the previous row at the top of Next() / during Dispose().
    private int _lastAutoIndex;

    internal RowGroupScope(string id, ReadOnlySpan<Sz> sizes, float gap)
    {
        _cacheKey = id;
        _gap = gap;

        var totalGap = gap * MathF.Max(0f, sizes.Length - 1);
        var available = LayoutContext.RemainingHeight() - totalGap;
        _parentWidth = LayoutContext.Width();
        _parentAvail = MathF.Max(0f, available);

        // Snapshot Sz into a local array. ReadOnlySpan can't be held across awaits
        // or returned from methods; the copy is tiny (one entry per row).
        _sizes = new Sz[sizes.Length];
        for (var i = 0; i < sizes.Length; i++)
        {
            _sizes[i] = sizes[i];
        }

        // Seed live auto heights from the per-id cache. On the very first frame the
        // cache is empty → zeros → Fill temporarily over-claims below-Fill autos and
        // self-corrects next frame. Autos ABOVE a Fill are always measured before
        // the Fill opens, so they never need to rely on the cache.
        if (!AutoHeightCache.TryGetValue(id, out var cached) || cached.Length != sizes.Length)
        {
            cached = new float[sizes.Length];
        }

        _autoHeights = new float[sizes.Length];
        for (var i = 0; i < sizes.Length; i++)
        {
            _autoHeights[i] = _sizes[i].IsAuto ? cached[i] : 0f;
        }

        _totalFixed = 0f;
        _totalFillWeight = 0f;
        for (var i = 0; i < sizes.Length; i++)
        {
            if (_sizes[i].IsFixed)
            {
                _totalFixed += _sizes[i].Value;
            }
            else if (_sizes[i].IsFill)
            {
                _totalFillWeight += _sizes[i].Value;
            }
        }

        _parentSpacing = ImGui.GetStyle().ItemSpacing;
        _nextIndex = 0;
        _lastAutoIndex = -1;
        _disposed = false;

        // Reserve all available height so no sibling after this group can claim it.
        LayoutContext.ConsumeHeight(_parentAvail + totalGap);

        // Zero out item spacing so rows + gap dummies tile exactly. Restored in Dispose.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
    }

    /// <summary>
    ///     Opens the next row. Auto rows grow to fit their content; Fixed rows use
    ///     their declared height; Fill rows split whatever space is left after
    ///     Fixed + Auto (measured-or-cached) across their weights.
    ///     Must be called exactly once per <see cref="Sz"/> passed at construction, in order.
    /// </summary>
    public RowItemScope Next(
        Style style = default,
        ImGuiChildFlags childFlags = ImGuiChildFlags.None
    )
    {
        // Capture the just-closed auto row's rendered height before we emit any new
        // items (the gap Dummy below would otherwise replace the "last item").
        CaptureLastAutoHeight();

        // Emit gap spacing before every row except the first.
        if (_nextIndex > 0 && _gap > 0f)
        {
            ImGui.Dummy(new Vector2(0f, _gap));
        }

        var size = _sizes[_nextIndex];
        var autoResize = size.IsAuto;
        var height = ResolveHeightFor(_nextIndex);

        if (autoResize)
        {
            _lastAutoIndex = _nextIndex;
        }

        _nextIndex++;

        return new RowItemScope(
            _parentWidth,
            height,
            style,
            childFlags,
            _parentSpacing,
            autoResize
        );
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Last row may be an auto; capture it before tearing down style state.
        CaptureLastAutoHeight();

        // Write measured heights back to the shared cache for the next frame.
        AutoHeightCache[_cacheKey] = _autoHeights;

        ImGui.PopStyleVar(); // ItemSpacing zero
    }

    private void CaptureLastAutoHeight()
    {
        if (_lastAutoIndex < 0)
        {
            return;
        }

        _autoHeights[_lastAutoIndex] = ImGui.GetItemRectSize().Y;
        _lastAutoIndex = -1;
    }

    private float ResolveHeightFor(int index)
    {
        var sz = _sizes[index];

        if (sz.IsFixed)
        {
            return sz.Value;
        }

        if (sz.IsAuto)
        {
            // Auto rows use AutoResizeY; the nominal height passed to BeginChild is 0.
            return 0f;
        }

        // Fill: share the remainder after Fixed + Auto reservations with other Fill siblings.
        if (_totalFillWeight <= 0f)
        {
            return 0f;
        }

        var autoTotal = 0f;
        for (var i = 0; i < _autoHeights.Length; i++)
        {
            autoTotal += _autoHeights[i];
        }

        var remaining = MathF.Max(0f, _parentAvail - _totalFixed - autoTotal);
        return remaining * (sz.Value / _totalFillWeight);
    }
}

/// <summary>
///     A single row within a <see cref="RowGroupScope"/>.
///     Handles the two-phase style push: container (padding, bg) before BeginChild,
///     content (item spacing) after BeginChild.
/// </summary>
public ref struct RowItemScope
{
    private int _outerVarCount;
    private int _outerColorCount;
    private bool _disposed;

    internal RowItemScope(
        float width,
        float height,
        Style style,
        ImGuiChildFlags childFlags,
        Vector2 parentSpacing,
        bool autoResize
    )
    {
        _disposed = false;
        _outerVarCount = 0;
        _outerColorCount = 0;

        var padding = style.IsDefault ? Vector2.Zero : style.Padding;
        var innerSpacing = style.IsDefault ? parentSpacing : style.ItemSpacing;

        // ── Before BeginChild ───────────────────────────────────────────
        // Zero IS so rows tile edge-to-edge.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        _outerVarCount++;

        // ALWAYS set WP explicitly — never let the parent's WP leak in.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
        _outerVarCount++;

        if (style.ChildBg is { } bg)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, bg);
            _outerColorCount++;
        }

        // Auto rows grow to fit their content via AutoResizeY. Fixed/Fill rows get
        // an explicit height; `height` is 0 for Auto so the BeginChild vector reads
        // (width, 0) in that case which is what AutoResizeY expects.
        var flags = childFlags;
        if (autoResize)
        {
            flags |= ImGuiChildFlags.AutoResizeY;
        }

        // Use a monotonically-growing id based on parent cursor so two siblings with
        // identical heights (e.g. two auto rows rendered tall) don't collide.
        ImGui.BeginChild(
            $"##RowItem_{height:F0}_{ImGui.GetCursorPosY():F0}",
            new Vector2(width, height),
            flags
        );

        // ── After BeginChild ────────────────────────────────────────────
        // Set content spacing for items inside this row.
        // Reset WP to zero so nested BeginChild calls don't inherit our padding.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, innerSpacing);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        var innerWidth = MathF.Max(0f, width - padding.X * 2f);
        // For auto rows we don't know innerHeight up front; 0 is a safe sentinel —
        // don't nest Fill rows inside an Auto row (they'd have nothing to claim).
        var innerHeight = autoResize ? 0f : MathF.Max(0f, height - padding.Y * 2f);
        LayoutContext.Push(innerWidth, innerHeight);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        LayoutContext.Pop();
        ImGui.PopStyleVar(2); // inner WP reset + inner IS
        ImGui.EndChild();
        if (_outerColorCount > 0)
        {
            ImGui.PopStyleColor(_outerColorCount);
        }

        ImGui.PopStyleVar(_outerVarCount); // outer IS(0) + outer WP
    }
}
