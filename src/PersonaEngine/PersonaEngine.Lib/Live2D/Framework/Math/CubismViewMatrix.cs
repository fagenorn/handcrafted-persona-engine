namespace PersonaEngine.Lib.Live2D.Framework.Math;

/// <summary>
///     カメラの位置変更に使うと便利な4x4行列のクラス。
/// </summary>
public record CubismViewMatrix : CubismMatrix44
{
    /// <summary>
    ///     デバイスに対応する論理座標上の範囲（左辺X軸位置）
    /// </summary>
    public float ScreenLeft { get; private set; }

    /// <summary>
    ///     デバイスに対応する論理座標上の範囲（右辺X軸位置）
    /// </summary>
    public float ScreenRight { get; private set; }

    /// <summary>
    ///     デバイスに対応する論理座標上の範囲（下辺Y軸位置）
    /// </summary>
    public float ScreenTop { get; private set; }

    /// <summary>
    ///     デバイスに対応する論理座標上の範囲（上辺Y軸位置）
    /// </summary>
    public float ScreenBottom { get; private set; }

    /// <summary>
    ///     論理座標上の移動可能範囲（左辺X軸位置）
    /// </summary>
    public float MaxLeft { get; private set; }

    /// <summary>
    ///     論理座標上の移動可能範囲（右辺X軸位置）
    /// </summary>
    public float MaxRight { get; private set; }

    /// <summary>
    ///     論理座標上の移動可能範囲（下辺Y軸位置）
    /// </summary>
    public float MaxTop { get; private set; }

    /// <summary>
    ///     論理座標上の移動可能範囲（上辺Y軸位置）
    /// </summary>
    public float MaxBottom { get; private set; }

    /// <summary>
    ///     拡大率の最大値
    /// </summary>
    public float MaxScale { get; set; }

    /// <summary>
    ///     拡大率の最小値
    /// </summary>
    public float MinScale { get; set; }

    /// <summary>
    ///     移動を調整する。
    /// </summary>
    /// <param name="x">X軸の移動量</param>
    /// <param name="y">Y軸の移動量</param>
    public void AdjustTranslate(float x, float y)
    {
        if (_tr[0] * MaxLeft + (_tr[12] + x) > ScreenLeft)
        {
            x = ScreenLeft - _tr[0] * MaxLeft - _tr[12];
        }

        if (_tr[0] * MaxRight + (_tr[12] + x) < ScreenRight)
        {
            x = ScreenRight - _tr[0] * MaxRight - _tr[12];
        }

        if (_tr[5] * MaxTop + (_tr[13] + y) < ScreenTop)
        {
            y = ScreenTop - _tr[5] * MaxTop - _tr[13];
        }

        if (_tr[5] * MaxBottom + (_tr[13] + y) > ScreenBottom)
        {
            y = ScreenBottom - _tr[5] * MaxBottom - _tr[13];
        }

        float[] tr1 =
        [
            1.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            1.0f,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            1.0f,
            0.0f,
            x,
            y,
            0.0f,
            1.0f,
        ];

        MultiplyByMatrix(tr1);
    }

    /// <summary>
    ///     拡大率を調整する。
    /// </summary>
    /// <param name="cx">拡大を行うX軸の中心位置</param>
    /// <param name="cy">拡大を行うY軸の中心位置</param>
    /// <param name="scale">拡大率</param>
    public void AdjustScale(float cx, float cy, float scale)
    {
        var maxScale = MaxScale;
        var minScale = MinScale;

        var targetScale = scale * _tr[0]; //

        if (targetScale < minScale)
        {
            if (_tr[0] > 0.0f)
            {
                scale = minScale / _tr[0];
            }
        }
        else if (targetScale > maxScale)
        {
            if (_tr[0] > 0.0f)
            {
                scale = maxScale / _tr[0];
            }
        }

        MultiplyByMatrix(
            [
                1.0f,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                1.0f,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                1.0f,
                0.0f,
                -cx,
                -cy,
                0.0f,
                1.0f,
            ]
        );

        MultiplyByMatrix(
            [
                scale,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                scale,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                1.0f,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                1.0f,
            ]
        );

        MultiplyByMatrix(
            [
                1.0f,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                1.0f,
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                1.0f,
                0.0f,
                cx,
                cy,
                0.0f,
                1.0f,
            ]
        );
    }

    /// <summary>
    ///     デバイスに対応する論理座標上の範囲の設定を行う。
    /// </summary>
    /// <param name="left">左辺のX軸の位置</param>
    /// <param name="right">右辺のX軸の位置</param>
    /// <param name="bottom">下辺のY軸の位置</param>
    /// <param name="top">上辺のY軸の位置</param>
    public void SetScreenRect(float left, float right, float bottom, float top)
    {
        ScreenLeft = left;
        ScreenRight = right;
        ScreenTop = top;
        ScreenBottom = bottom;
    }

    /// <summary>
    ///     デバイスに対応する論理座標上の移動可能範囲の設定を行う。
    /// </summary>
    /// <param name="left">左辺のX軸の位置</param>
    /// <param name="right">右辺のX軸の位置</param>
    /// <param name="bottom">下辺のY軸の位置</param>
    /// <param name="top">上辺のY軸の位置</param>
    public void SetMaxScreenRect(float left, float right, float bottom, float top)
    {
        MaxLeft = left;
        MaxRight = right;
        MaxTop = top;
        MaxBottom = bottom;
    }

    /// <summary>
    ///     拡大率が最大になっているかどうかを確認する。
    /// </summary>
    /// <returns>
    ///     true    拡大率は最大になっている
    ///     false   拡大率は最大になっていない
    /// </returns>
    public bool IsMaxScale()
    {
        return GetScaleX() >= MaxScale;
    }

    /// <summary>
    ///     拡大率が最小になっているかどうかを確認する。
    /// </summary>
    /// <returns>
    ///     true    拡大率は最小になっている
    ///     false   拡大率は最小になっていない
    /// </returns>
    public bool IsMinScale()
    {
        return GetScaleX() <= MinScale;
    }
}
