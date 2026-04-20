using PersonaEngine.Lib.Live2D.Framework.Rendering;

namespace PersonaEngine.Lib.Live2D.Framework.Model;

/// <summary>
///     テクスチャの色をRGBAで扱うための構造体
/// </summary>
public record PartColorData
{
    public CubismTextureColor Color = new();

    public bool IsOverwritten { get; set; }
}
