namespace PersonaEngine.Lib.Live2D.Framework.Rendering;

/// <summary>
///     テクスチャの色をRGBAで扱うための構造体
/// </summary>
public struct CubismTextureColor
{
    /// <summary>
    ///     赤チャンネル
    /// </summary>
    public float R;

    /// <summary>
    ///     緑チャンネル
    /// </summary>
    public float G;

    /// <summary>
    ///     青チャンネル
    /// </summary>
    public float B;

    /// <summary>
    ///     αチャンネル
    /// </summary>
    public float A;

    public CubismTextureColor()
    {
        R = 1.0f;
        G = 1.0f;
        B = 1.0f;
        A = 1.0f;
    }

    public CubismTextureColor(CubismTextureColor old)
    {
        R = old.R;
        G = old.G;
        B = old.B;
        A = old.A;
        Check();
    }

    public CubismTextureColor(float r, float g, float b, float a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
        Check();
    }

    private void Check()
    {
        R = R > 1.0f ? 1f : R;
        G = G > 1.0f ? 1f : G;
        B = B > 1.0f ? 1f : B;
        A = A > 1.0f ? 1f : A;
    }
}
