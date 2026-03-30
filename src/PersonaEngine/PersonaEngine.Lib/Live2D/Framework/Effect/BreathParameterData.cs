namespace PersonaEngine.Lib.Live2D.Framework.Effect;

/// <summary>
///     呼吸のパラメータ情報。
/// </summary>
public record BreathParameterData
{
    /// <summary>
    ///     呼吸をひもづけるパラメータIDs
    /// </summary>
    public required string ParameterId { get; set; }

    /// <summary>
    ///     呼吸を正弦波としたときの、波のオフセット
    /// </summary>
    public float Offset { get; set; }

    /// <summary>
    ///     呼吸を正弦波としたときの、波の高さ
    /// </summary>
    public float Peak { get; set; }

    /// <summary>
    ///     呼吸を正弦波としたときの、波の周期
    /// </summary>
    public float Cycle { get; set; }

    /// <summary>
    ///     パラメータへの重み
    /// </summary>
    public float Weight { get; set; }
}
