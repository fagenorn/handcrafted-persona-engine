namespace PersonaEngine.Lib.Live2D.App;

/// <summary>
///     画像情報構造体
/// </summary>
public record TextureInfo
{
    /// <summary>
    ///     ファイル名
    /// </summary>
    public required string FileName;

    /// <summary>
    ///     高さ
    /// </summary>
    public int Height;

    /// <summary>
    ///     テクスチャID
    /// </summary>
    public int ID;

    /// <summary>
    ///     横幅
    /// </summary>
    public int Width;
}
