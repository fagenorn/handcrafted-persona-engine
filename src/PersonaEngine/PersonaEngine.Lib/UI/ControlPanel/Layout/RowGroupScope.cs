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
    ///     Maximum number of distinct group ids retained across frames. When the cache
    ///     grows beyond this cap the oldest entry is evicted. Callers should use a
    ///     finite, stable id space (one id per call site) — this cap is a safety net
    ///     for misuse (e.g. accidentally per-frame varying ids), not a design point.
    /// </summary>
    private const int CacheMaxEntries = 256;

    /// <summary>
    ///     Pre-computed row child ids up to 16, to avoid per-row-per-frame string
    ///     interpolation. Larger groups fall back to interpolation (practically never
    ///     occurs in this codebase).
    /// </summary>
    private static readonly string[] RowIds =
    {
        "##r0",
        "##r1",
        "##r2",
        "##r3",
        "##r4",
        "##r5",
        "##r6",
        "##r7",
        "##r8",
        "##r9",
        "##r10",
        "##r11",
        "##r12",
        "##r13",
        "##r14",
        "##r15",
    };

    /// <summary>
    ///     Cache of per-group auto-row heights, keyed by the caller-supplied group id.
    ///     Arrays are reused in place across frames so Fill-row math has stable
    ///     reservations without per-frame allocations. Accessed only from the UI
    ///     thread (ImGui is single-threaded), so a plain <see cref="Dictionary{TKey,TValue}"/>
    ///     with an <see cref="_cacheLock"/> for eviction safety is sufficient.
    ///     Bounded at <see cref="CacheMaxEntries"/> via drop-oldest LRU so a misbehaving
    ///     caller with a varying id space cannot leak unboundedly.
    /// </summary>
    private static readonly Dictionary<string, float[]> AutoHeightCache = new(CacheMaxEntries);

    /// <summary>
    ///     Insertion-order list of ids used for drop-oldest eviction. Node per entry
    ///     is stored in <see cref="AutoHeightCacheNodes"/> so eviction is O(1).
    /// </summary>
    private static readonly LinkedList<string> AutoHeightCacheOrder = new();

    private static readonly Dictionary<string, LinkedListNode<string>> AutoHeightCacheNodes = new(
        CacheMaxEntries
    );

    private static readonly object _cacheLock = new();

    private readonly ReadOnlySpan<Sz> _sizes;

    // Reference to the cached auto-heights array for this group. Writes go straight
    // into the cache slot so there is no per-frame allocation or copy-back on Dispose.
    // The array length matches _sizes.Length; mismatches are resolved at construction.
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
        _sizes = sizes;
        _gap = gap;

        var totalGap = gap * MathF.Max(0f, sizes.Length - 1);
        var available = LayoutContext.RemainingHeight() - totalGap;
        _parentWidth = LayoutContext.Width();
        _parentAvail = MathF.Max(0f, available);

        // Resolve the per-id auto-heights array. On the very first frame (or when the
        // group's row count changed) we start from zeros → Fill temporarily over-claims
        // below-Fill autos and self-corrects next frame. Autos ABOVE a Fill are always
        // measured before the Fill opens, so they never need to rely on the cache.
        _autoHeights = GetOrCreateAutoHeights(id, sizes.Length);

        // For non-auto slots, keep zero so the Fill remainder calculation is correct
        // regardless of stale data carried in the cached array from a prior shape.
        for (var i = 0; i < sizes.Length; i++)
        {
            if (!sizes[i].IsAuto)
            {
                _autoHeights[i] = 0f;
            }
        }

        _totalFixed = 0f;
        _totalFillWeight = 0f;
        for (var i = 0; i < sizes.Length; i++)
        {
            if (sizes[i].IsFixed)
            {
                _totalFixed += sizes[i].Value;
            }
            else if (sizes[i].IsFill)
            {
                _totalFillWeight += sizes[i].Value;
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

        var index = _nextIndex;
        var size = _sizes[index];
        var autoResize = size.IsAuto;
        var height = ResolveHeightFor(index);

        if (autoResize)
        {
            _lastAutoIndex = index;
        }

        _nextIndex++;

        // Stable id per row-index within this scope; parent window/context scopes the
        // id further so siblings in different groups don't collide.
        var rowId = index < RowIds.Length ? RowIds[index] : $"##r{index}";

        return new RowItemScope(
            rowId,
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
        // The write lands directly in the cache slot (_autoHeights IS the cache array),
        // so there is no explicit copy-back.
        CaptureLastAutoHeight();

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

    private readonly float ResolveHeightFor(int index)
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

    /// <summary>
    ///     Look up (or create) the auto-heights array for <paramref name="id"/>. The
    ///     returned array is reused across frames — callers write measured heights into
    ///     it in place. Bounded at <see cref="CacheMaxEntries"/> via drop-oldest eviction
    ///     if a caller feeds varying ids.
    /// </summary>
    private static float[] GetOrCreateAutoHeights(string id, int length)
    {
        lock (_cacheLock)
        {
            if (AutoHeightCache.TryGetValue(id, out var cached) && cached.Length == length)
            {
                return cached;
            }

            // Shape changed (or first observation) → fresh zero-filled array. We do NOT
            // try to preserve old values: a shape change means the caller's layout has
            // been edited and the old measurements are no longer meaningful.
            var array = new float[length];

            // If the id already exists (shape change), swap the array but keep the
            // existing insertion-order node so a recently-edited call site doesn't get
            // bumped to the head of the eviction queue.
            if (AutoHeightCacheNodes.ContainsKey(id))
            {
                AutoHeightCache[id] = array;
                return array;
            }

            // Bounded at CacheMaxEntries via drop-oldest FIFO so a caller that feeds
            // varying ids (e.g. accidentally per-frame strings) cannot leak unboundedly.
            if (AutoHeightCache.Count >= CacheMaxEntries)
            {
                var oldest = AutoHeightCacheOrder.First;
                if (oldest is not null)
                {
                    AutoHeightCacheOrder.RemoveFirst();
                    AutoHeightCacheNodes.Remove(oldest.Value);
                    AutoHeightCache.Remove(oldest.Value);
                }
            }

            AutoHeightCache[id] = array;
            AutoHeightCacheNodes[id] = AutoHeightCacheOrder.AddLast(id);
            return array;
        }
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
        string rowId,
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

        // Stable per-row id. The parent group scope disambiguates across sibling groups,
        // and the row index disambiguates within a group, so two rows never collide.
        ImGui.BeginChild(rowId, new Vector2(width, height), flags);

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
