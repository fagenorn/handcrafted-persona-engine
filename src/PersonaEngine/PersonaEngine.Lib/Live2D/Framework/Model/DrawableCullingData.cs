namespace PersonaEngine.Lib.Live2D.Framework.Model;

/// <summary>
///     テクスチャのカリング設定を管理するための構造体
/// </summary>
public record DrawableCullingData
{
    public bool IsOverwritten { get; set; }

    public bool IsCulling { get; set; }
}
