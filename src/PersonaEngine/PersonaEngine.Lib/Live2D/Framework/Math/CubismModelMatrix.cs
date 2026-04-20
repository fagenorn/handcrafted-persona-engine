namespace PersonaEngine.Lib.Live2D.Framework.Math;

/// <summary>
///     モデル座標設定用の4x4行列クラス。
/// </summary>
public record CubismModelMatrix : CubismMatrix44
{
    public const string KeyWidth = "width";

    public const string KeyHeight = "height";

    public const string KeyX = "x";

    public const string KeyY = "y";

    public const string KeyCenterX = "center_x";

    public const string KeyCenterY = "center_y";

    public const string KeyTop = "top";

    public const string KeyBottom = "bottom";

    public const string KeyLeft = "left";

    public const string KeyRight = "right";

    /// <summary>
    ///     縦幅
    /// </summary>
    private readonly float _height;

    /// <summary>
    ///     横幅
    /// </summary>
    private readonly float _width;

    public CubismModelMatrix(float w, float h)
    {
        _width = w;
        _height = h;

        SetHeight(2.0f);
    }

    /// <summary>
    ///     横幅を設定する。
    /// </summary>
    /// <param name="w">横幅</param>
    public void SetWidth(float w)
    {
        var scaleX = w / _width;
        var scaleY = scaleX;
        Scale(scaleX, scaleY);
    }

    /// <summary>
    ///     縦幅を設定する。
    /// </summary>
    /// <param name="h">縦幅</param>
    public void SetHeight(float h)
    {
        var scaleX = h / _height;
        var scaleY = scaleX;
        Scale(scaleX, scaleY);
    }

    /// <summary>
    ///     位置を設定する。
    /// </summary>
    /// <param name="x">X軸の位置</param>
    /// <param name="y">Y軸の位置</param>
    public void SetPosition(float x, float y)
    {
        Translate(x, y);
    }

    /// <summary>
    ///     中心位置を設定する。
    /// </summary>
    /// <param name="x">X軸の中心位置</param>
    /// <param name="y">Y軸の中心位置</param>
    public void SetCenterPosition(float x, float y)
    {
        CenterX(x);
        CenterY(y);
    }

    /// <summary>
    ///     上辺の位置を設定する。
    /// </summary>
    /// <param name="y">上辺のY軸位置</param>
    public void Top(float y)
    {
        SetY(y);
    }

    /// <summary>
    ///     下辺の位置を設定する。
    /// </summary>
    /// <param name="y">下辺のY軸位置</param>
    public void Bottom(float y)
    {
        var h = _height * GetScaleY();
        TranslateY(y - h);
    }

    /// <summary>
    ///     左辺の位置を設定する。
    /// </summary>
    /// <param name="x">左辺のX軸位置</param>
    public void Left(float x)
    {
        SetX(x);
    }

    /// <summary>
    ///     右辺の位置を設定する。
    /// </summary>
    /// <param name="x">右辺のX軸位置</param>
    public void Right(float x)
    {
        var w = _width * GetScaleX();
        TranslateX(x - w);
    }

    /// <summary>
    ///     X軸の中心位置を設定する。
    /// </summary>
    /// <param name="x">X軸の中心位置</param>
    public void CenterX(float x)
    {
        var w = _width * GetScaleX();
        TranslateX(x - w / 2.0f);
    }

    /// <summary>
    ///     X軸の位置を設定する。
    /// </summary>
    /// <param name="x">X軸の位置</param>
    public void SetX(float x)
    {
        TranslateX(x);
    }

    /// <summary>
    ///     Y軸の中心位置を設定する。
    /// </summary>
    /// <param name="y">Y軸の中心位置</param>
    public void CenterY(float y)
    {
        var h = _height * GetScaleY();
        TranslateY(y - h / 2.0f);
    }

    /// <summary>
    ///     Y軸の位置を設定する。
    /// </summary>
    /// <param name="y">Y軸の位置</param>
    public void SetY(float y)
    {
        TranslateY(y);
    }

    /// <summary>
    ///     レイアウト情報から位置を設定する。
    /// </summary>
    /// <param name="layout">レイアウト情報</param>
    public void SetupFromLayout(Dictionary<string, float> layout)
    {
        foreach (var item in layout)
        {
            if (item.Key == KeyWidth)
            {
                SetWidth(item.Value);
            }
            else if (item.Key == KeyHeight)
            {
                SetHeight(item.Value);
            }
        }

        foreach (var item in layout)
        {
            if (item.Key == KeyX)
            {
                SetX(item.Value);
            }
            else if (item.Key == KeyY)
            {
                SetY(item.Value);
            }
            else if (item.Key == KeyCenterX)
            {
                CenterX(item.Value);
            }
            else if (item.Key == KeyCenterY)
            {
                CenterY(item.Value);
            }
            else if (item.Key == KeyTop)
            {
                Top(item.Value);
            }
            else if (item.Key == KeyBottom)
            {
                Bottom(item.Value);
            }
            else if (item.Key == KeyLeft)
            {
                Left(item.Value);
            }
            else if (item.Key == KeyRight)
            {
                Right(item.Value);
            }
        }
    }
}
