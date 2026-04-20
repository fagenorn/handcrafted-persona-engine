namespace PersonaEngine.Lib.Live2D.App;

public class TouchManager
{
    /// <summary>
    ///     前回の値から今回の値へのxの移動距離。
    /// </summary>
    private float _deltaX;

    /// <summary>
    ///     前回の値から今回の値へのyの移動距離。
    /// </summary>
    private float _deltaY;

    /// <summary>
    ///     フリップが有効かどうか
    /// </summary>
    private bool _flipAvailable;

    /// <summary>
    ///     2本以上でタッチしたときの指の距離
    /// </summary>
    private float _lastTouchDistance;

    /// <summary>
    ///     シングルタッチ時のxの値
    /// </summary>
    private float _lastX;

    /// <summary>
    ///     ダブルタッチ時の一つ目のxの値
    /// </summary>
    private float _lastX1;

    /// <summary>
    ///     ダブルタッチ時の二つ目のxの値
    /// </summary>
    private float _lastX2;

    /// <summary>
    ///     シングルタッチ時のyの値
    /// </summary>
    private float _lastY;

    /// <summary>
    ///     ダブルタッチ時の一つ目のyの値
    /// </summary>
    private float _lastY1;

    /// <summary>
    ///     ダブルタッチ時の二つ目のyの値
    /// </summary>
    private float _lastY2;

    /// <summary>
    ///     このフレームで掛け合わせる拡大率。拡大操作中以外は1。
    /// </summary>
    private float _scale;

    /// <summary>
    ///     タッチを開始した時のyの値
    /// </summary>
    private float _startX;

    /// <summary>
    ///     タッチを開始した時のxの値
    /// </summary>
    private float _startY;

    /// <summary>
    ///     シングルタッチ時はtrue
    /// </summary>
    private bool _touchSingle;

    public TouchManager()
    {
        _scale = 1.0f;
    }

    public float GetCenterX()
    {
        return _lastX;
    }

    public float GetCenterY()
    {
        return _lastY;
    }

    public float GetDeltaX()
    {
        return _deltaX;
    }

    public float GetDeltaY()
    {
        return _deltaY;
    }

    public float GetStartX()
    {
        return _startX;
    }

    public float GetStartY()
    {
        return _startY;
    }

    public float GetScale()
    {
        return _scale;
    }

    public float GetX()
    {
        return _lastX;
    }

    public float GetY()
    {
        return _lastY;
    }

    public float GetX1()
    {
        return _lastX1;
    }

    public float GetY1()
    {
        return _lastY1;
    }

    public float GetX2()
    {
        return _lastX2;
    }

    public float GetY2()
    {
        return _lastY2;
    }

    public bool IsSingleTouch()
    {
        return _touchSingle;
    }

    public bool IsFlickAvailable()
    {
        return _flipAvailable;
    }

    public void DisableFlick()
    {
        _flipAvailable = false;
    }

    /// <summary>
    ///     タッチ開始時イベント
    /// </summary>
    /// <param name="deviceX">タッチした画面のyの値</param>
    /// <param name="deviceY">タッチした画面のxの値</param>
    public void TouchesBegan(float deviceX, float deviceY)
    {
        _lastX = deviceX;
        _lastY = deviceY;
        _startX = deviceX;
        _startY = deviceY;
        _lastTouchDistance = -1.0f;
        _flipAvailable = true;
        _touchSingle = true;
    }

    /// <summary>
    ///     ドラッグ時のイベント
    /// </summary>
    /// <param name="deviceX">タッチした画面のxの値</param>
    /// <param name="deviceY">タッチした画面のyの値</param>
    public void TouchesMoved(float deviceX, float deviceY)
    {
        _lastX = deviceX;
        _lastY = deviceY;
        _lastTouchDistance = -1.0f;
        _touchSingle = true;
    }

    /// <summary>
    ///     ドラッグ時のイベント
    /// </summary>
    /// <param name="deviceX1">1つめのタッチした画面のxの値</param>
    /// <param name="deviceY1">1つめのタッチした画面のyの値</param>
    /// <param name="deviceX2">2つめのタッチした画面のxの値</param>
    /// <param name="deviceY2">2つめのタッチした画面のyの値</param>
    public void TouchesMoved(float deviceX1, float deviceY1, float deviceX2, float deviceY2)
    {
        var distance = CalculateDistance(deviceX1, deviceY1, deviceX2, deviceY2);
        var centerX = (deviceX1 + deviceX2) * 0.5f;
        var centerY = (deviceY1 + deviceY2) * 0.5f;

        if (_lastTouchDistance > 0.0f)
        {
            _scale = MathF.Pow(distance / _lastTouchDistance, 0.75f);
            _deltaX = CalculateMovingAmount(deviceX1 - _lastX1, deviceX2 - _lastX2);
            _deltaY = CalculateMovingAmount(deviceY1 - _lastY1, deviceY2 - _lastY2);
        }
        else
        {
            _scale = 1.0f;
            _deltaX = 0.0f;
            _deltaY = 0.0f;
        }

        _lastX = centerX;
        _lastY = centerY;
        _lastX1 = deviceX1;
        _lastY1 = deviceY1;
        _lastX2 = deviceX2;
        _lastY2 = deviceY2;
        _lastTouchDistance = distance;
        _touchSingle = false;
    }

    /// <summary>
    ///     フリックの距離測定
    /// </summary>
    /// <returns>フリック距離</returns>
    public float GetFlickDistance()
    {
        return CalculateDistance(_startX, _startY, _lastX, _lastY);
    }

    /// <summary>
    ///     点1から点2への距離を求める
    /// </summary>
    /// <param name="x1">1つめのタッチした画面のxの値</param>
    /// <param name="y1">1つめのタッチした画面のyの値</param>
    /// <param name="x2">2つめのタッチした画面のxの値</param>
    /// <param name="y2">2つめのタッチした画面のyの値</param>
    /// <returns>2点の距離</returns>
    public static float CalculateDistance(float x1, float y1, float x2, float y2)
    {
        return MathF.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
    }

    /// <summary>
    ///     二つの値から、移動量を求める。
    ///     違う方向の場合は移動量０。同じ方向の場合は、絶対値が小さい方の値を参照する
    /// </summary>
    /// <param name="v1">1つめの移動量</param>
    /// <param name="v2">2つめの移動量</param>
    /// <returns>小さい方の移動量</returns>
    public static float CalculateMovingAmount(float v1, float v2)
    {
        if (v1 > 0.0f != v2 > 0.0f)
        {
            return 0.0f;
        }

        var sign = v1 > 0.0f ? 1.0f : -1.0f;
        var absoluteValue1 = MathF.Abs(v1);
        var absoluteValue2 = MathF.Abs(v2);

        return sign * (absoluteValue1 < absoluteValue2 ? absoluteValue1 : absoluteValue2);
    }
}
