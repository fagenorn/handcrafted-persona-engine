namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Sizing value: fixed pixels, weighted fill of remaining space, or auto-size to content.
///     Auto rows grow to fit their child content each frame; Fill rows claim whatever
///     space remains after Fixed + Auto rows have taken theirs.
/// </summary>
public readonly struct Sz
{
    internal enum Mode
    {
        Fixed,
        Fill,
        Auto,
    }

    /// <summary>Pixel value (when <see cref="IsFixed"/>) or weight (when fill). Unused for Auto.</summary>
    public readonly float Value;

    internal readonly Mode Kind;

    /// <summary>True = fixed pixels.</summary>
    public bool IsFixed => Kind == Mode.Fixed;

    /// <summary>True = weighted fill of remaining space.</summary>
    public bool IsFill => Kind == Mode.Fill;

    /// <summary>True = auto-size to content (AutoResizeY child).</summary>
    public bool IsAuto => Kind == Mode.Auto;

    private Sz(float value, Mode kind)
    {
        Value = value;
        Kind = kind;
    }

    /// <summary>Fixed size in pixels.</summary>
    public static Sz Fixed(float pixels) => new(pixels, Mode.Fixed);

    /// <summary>Fill remaining space with the given weight (default 1).</summary>
    public static Sz Fill(float weight = 1f) => new(weight, Mode.Fill);

    /// <summary>
    ///     Auto-size to content height. Rendered via an <c>AutoResizeY</c> child so the
    ///     row grows to fit whatever it contains. Auto rows' actual measured heights are
    ///     cached per <see cref="RowGroupScope"/> ID so that sibling <see cref="Fill"/>
    ///     rows can compute their share correctly without a second pass.
    /// </summary>
    public static Sz Auto() => new(0f, Mode.Auto);

    /// <summary>
    ///     Pure resolver for Fixed + Fill only. Retained for the simple case where
    ///     a caller has no <see cref="Auto"/> rows; Auto resolution requires live
    ///     measurement and is handled inside <see cref="RowGroupScope"/>.
    ///     Fixed values are used as-is; remaining pixels are distributed across
    ///     Fill values proportionally by weight.
    /// </summary>
    public static float[] Resolve(ReadOnlySpan<Sz> sizes, float available)
    {
        var results = new float[sizes.Length];
        var fixedTotal = 0f;
        var fillWeightTotal = 0f;

        for (var i = 0; i < sizes.Length; i++)
        {
            if (sizes[i].IsFixed)
            {
                results[i] = sizes[i].Value;
                fixedTotal += sizes[i].Value;
            }
            else if (sizes[i].IsFill)
            {
                fillWeightTotal += sizes[i].Value;
            }
        }

        var remaining = MathF.Max(0f, available - fixedTotal);

        if (fillWeightTotal > 0f)
        {
            for (var i = 0; i < sizes.Length; i++)
            {
                if (sizes[i].IsFill)
                {
                    results[i] = remaining * (sizes[i].Value / fillWeightTotal);
                }
            }
        }

        return results;
    }
}
