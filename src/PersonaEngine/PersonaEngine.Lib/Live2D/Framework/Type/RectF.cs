namespace PersonaEngine.Lib.Live2D.Framework.Type;

/// <summary>
///     矩形形状(座標・長さはfloat値)を定義するクラス
/// </summary>
public class RectF
{
    /// <summary>
    ///     高さ
    /// </summary>
    public float Height;

    /// <summary>
    ///     幅
    /// </summary>
    public float Width;

    /// <summary>
    ///     左端X座標
    /// </summary>
    public float X;

    /// <summary>
    ///     上端Y座標
    /// </summary>
    public float Y;

    public RectF() { }

    /// <summary>
    ///     引数付きコンストラクタ
    /// </summary>
    /// <param name="x">左端X座標</param>
    /// <param name="y">上端Y座標</param>
    /// <param name="w">幅</param>
    /// <param name="h">高さ</param>
    public RectF(float x, float y, float w, float h)
    {
        X = x;
        Y = y;
        Width = w;
        Height = h;
    }

    /// <summary>
    ///     矩形に値をセットする
    /// </summary>
    /// <param name="r">矩形のインスタンス</param>
    public void SetRect(RectF r)
    {
        X = r.X;
        Y = r.Y;
        Width = r.Width;
        Height = r.Height;
    }

    /// <summary>
    ///     矩形中央を軸にして縦横を拡縮する
    /// </summary>
    /// <param name="w">幅方向に拡縮する量</param>
    /// <param name="h">高さ方向に拡縮する量</param>
    public void Expand(float w, float h)
    {
        X -= w;
        Y -= h;
        Width += w * 2.0f;
        Height += h * 2.0f;
    }

    /// <summary>
    ///     矩形中央のX座標を取得する
    /// </summary>
    public float GetCenterX()
    {
        return X + 0.5f * Width;
    }

    /// <summary>
    ///     矩形中央のY座標を取得する
    /// </summary>
    public float GetCenterY()
    {
        return Y + 0.5f * Height;
    }

    /// <summary>
    ///     右端のX座標を取得する
    /// </summary>
    public float GetRight()
    {
        return X + Width;
    }

    /// <summary>
    ///     下端のY座標を取得する
    /// </summary>
    public float GetBottom()
    {
        return Y + Height;
    }
}
