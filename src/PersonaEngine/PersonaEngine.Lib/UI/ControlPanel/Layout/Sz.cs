namespace PersonaEngine.Lib.UI.ControlPanel.Layout;

/// <summary>
///     Sizing value: either a fixed pixel count or a weighted fill of remaining space.
/// </summary>
public readonly struct Sz
{
    /// <summary>Pixel value (when <see cref="IsFixed"/>) or weight (when fill).</summary>
    public readonly float Value;

    /// <summary>True = fixed pixels, false = weighted fill.</summary>
    public readonly bool IsFixed;

    private Sz(float value, bool isFixed)
    {
        Value = value;
        IsFixed = isFixed;
    }

    /// <summary>Fixed size in pixels.</summary>
    public static Sz Fixed(float pixels) => new(pixels, true);

    /// <summary>Fill remaining space with the given weight (default 1).</summary>
    public static Sz Fill(float weight = 1f) => new(weight, false);

    /// <summary>
    ///     Resolves an array of <see cref="Sz"/> values against a total available size.
    ///     Fixed values are used as-is. Remaining pixels are distributed across
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
            else
            {
                fillWeightTotal += sizes[i].Value;
            }
        }

        var remaining = MathF.Max(0f, available - fixedTotal);

        if (fillWeightTotal > 0f)
        {
            for (var i = 0; i < sizes.Length; i++)
            {
                if (!sizes[i].IsFixed)
                {
                    results[i] = remaining * (sizes[i].Value / fillWeightTotal);
                }
            }
        }

        return results;
    }
}
