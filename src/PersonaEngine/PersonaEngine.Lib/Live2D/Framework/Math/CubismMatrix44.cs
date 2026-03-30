namespace PersonaEngine.Lib.Live2D.Framework.Math;

/// <summary>
///     4x4行列の便利クラス。
/// </summary>
public record CubismMatrix44
{
    private readonly float[] _mpt1 =
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
        0.0f,
        0.0f,
        0.0f,
        1.0f,
    ];

    private readonly float[] _mpt2 =
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
        0.0f,
        0.0f,
        0.0f,
        1.0f,
    ];

    private readonly float[] Ident =
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
        0.0f,
        0.0f,
        0.0f,
        1.0f,
    ];

    public float[] _mpt = new float[16];

    /// <summary>
    ///     4x4行列データ
    /// </summary>
    protected float[] _tr = new float[16];

    /// <summary>
    ///     コンストラクタ。
    /// </summary>
    public CubismMatrix44()
    {
        LoadIdentity();
    }

    public float[] Tr => _tr;

    /// <summary>
    ///     単位行列に初期化する。
    /// </summary>
    public void LoadIdentity()
    {
        SetMatrix(Ident);
    }

    /// <summary>
    ///     行列を設定する。
    /// </summary>
    /// <param name="tr">16個の浮動小数点数で表される4x4の行列</param>
    public void SetMatrix(float[] tr)
    {
        Array.Copy(tr, _tr, 16);
    }

    public void SetMatrix(CubismMatrix44 tr)
    {
        SetMatrix(tr.Tr);
    }

    /// <summary>
    ///     X軸の拡大率を取得する。
    /// </summary>
    /// <returns>X軸の拡大率</returns>
    public float GetScaleX()
    {
        return _tr[0];
    }

    /// <summary>
    ///     Y軸の拡大率を取得する。
    /// </summary>
    /// <returns>Y軸の拡大率</returns>
    public float GetScaleY()
    {
        return _tr[5];
    }

    /// <summary>
    ///     X軸の移動量を取得する。
    /// </summary>
    /// <returns>X軸の移動量</returns>
    public float GetTranslateX()
    {
        return _tr[12];
    }

    /// <summary>
    ///     Y軸の移動量を取得する。
    /// </summary>
    /// <returns>Y軸の移動量</returns>
    public float GetTranslateY()
    {
        return _tr[13];
    }

    /// <summary>
    ///     X軸の値を現在の行列で計算する。
    /// </summary>
    /// <param name="src">X軸の値</param>
    /// <returns>現在の行列で計算されたX軸の値</returns>
    public float TransformX(float src)
    {
        return _tr[0] * src + _tr[12];
    }

    /// <summary>
    ///     Y軸の値を現在の行列で計算する。
    /// </summary>
    /// <param name="src">Y軸の値</param>
    /// <returns>現在の行列で計算されたY軸の値</returns>
    public float TransformY(float src)
    {
        return _tr[5] * src + _tr[13];
    }

    /// <summary>
    ///     X軸の値を現在の行列で逆計算する。
    /// </summary>
    /// <param name="src">X軸の値</param>
    /// <returns>現在の行列で逆計算されたX軸の値</returns>
    public float InvertTransformX(float src)
    {
        return (src - _tr[12]) / _tr[0];
    }

    /// <summary>
    ///     Y軸の値を現在の行列で逆計算する。
    /// </summary>
    /// <param name="src">Y軸の値</param>
    /// <returns>現在の行列で逆計算されたY軸の値</returns>
    public float InvertTransformY(float src)
    {
        return (src - _tr[13]) / _tr[5];
    }

    /// <summary>
    ///     現在の行列の位置を起点にして相対的に移動する。
    /// </summary>
    /// <param name="x">X軸の移動量</param>
    /// <param name="y">Y軸の移動量</param>
    public void TranslateRelative(float x, float y)
    {
        _mpt1[12] = x;
        _mpt1[13] = y;
        MultiplyByMatrix(_mpt1);
    }

    /// <summary>
    ///     現在の行列の位置を指定した位置へ移動する。
    /// </summary>
    /// <param name="x">X軸の移動量</param>
    /// <param name="y">Y軸の移動量</param>
    public void Translate(float x, float y)
    {
        _tr[12] = x;
        _tr[13] = y;
    }

    /// <summary>
    ///     現在の行列のX軸の位置を指定した位置へ移動する。
    /// </summary>
    /// <param name="x">X軸の移動量</param>
    public void TranslateX(float x)
    {
        _tr[12] = x;
    }

    /// <summary>
    ///     現在の行列のY軸の位置を指定した位置へ移動する。
    /// </summary>
    /// <param name="y">Y軸の移動量</param>
    public void TranslateY(float y)
    {
        _tr[13] = y;
    }

    /// <summary>
    ///     現在の行列の拡大率を相対的に設定する。
    /// </summary>
    /// <param name="x">X軸の拡大率</param>
    /// <param name="y">Y軸の拡大率</param>
    public void ScaleRelative(float x, float y)
    {
        _mpt2[0] = x;
        _mpt2[5] = y;
        MultiplyByMatrix(_mpt2);
    }

    /// <summary>
    ///     現在の行列の拡大率を指定した倍率に設定する。
    /// </summary>
    /// <param name="x">X軸の拡大率</param>
    /// <param name="y">Y軸の拡大率</param>
    public void Scale(float x, float y)
    {
        _tr[0] = x;
        _tr[5] = y;
    }

    public void MultiplyByMatrix(float[] a)
    {
        Array.Fill(_mpt, 0);

        var n = 4;

        for (var i = 0; i < n; ++i)
        {
            for (var j = 0; j < n; ++j)
            {
                for (var k = 0; k < n; ++k)
                {
                    _mpt[j + i * 4] += a[k + i * 4] * _tr[j + k * 4];
                }
            }
        }

        Array.Copy(_mpt, _tr, 16);
    }

    /// <summary>
    ///     現在の行列に行列を乗算する。
    /// </summary>
    /// <param name="m">行列</param>
    public void MultiplyByMatrix(CubismMatrix44 m)
    {
        MultiplyByMatrix(m.Tr);
    }
}
