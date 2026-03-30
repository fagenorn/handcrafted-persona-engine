namespace PersonaEngine.Lib.Live2D.Framework.Math;

/// <summary>
///     顔の向きの制御機能を提供するクラス。
/// </summary>
public class CubismTargetPoint
{
    public const int FrameRate = 30;

    public const float Epsilon = 0.01f;

    /// <summary>
    ///     顔の向きのX目標値(この値に近づいていく)
    /// </summary>
    private float _faceTargetX;

    /// <summary>
    ///     顔の向きのY目標値(この値に近づいていく)
    /// </summary>
    private float _faceTargetY;

    /// <summary>
    ///     顔の向きの変化速度X
    /// </summary>
    private float _faceVX;

    /// <summary>
    ///     顔の向きの変化速度Y
    /// </summary>
    private float _faceVY;

    /// <summary>
    ///     最後の実行時間[秒]
    /// </summary>
    private float _lastTimeSeconds;

    /// <summary>
    ///     デルタ時間の積算値[秒]
    /// </summary>
    private float _userTimeSeconds;

    /// <summary>
    ///     顔の向きX(-1.0 - 1.0)
    /// </summary>
    public float FaceX { get; private set; }

    /// <summary>
    ///     顔の向きY(-1.0 - 1.0)
    /// </summary>
    public float FaceY { get; private set; }

    /// <summary>
    ///     更新処理を行う。
    /// </summary>
    /// <param name="deltaTimeSeconds">デルタ時間[秒]</param>
    public void Update(float deltaTimeSeconds)
    {
        // デルタ時間を加算する
        _userTimeSeconds += deltaTimeSeconds;

        // 首を中央から左右に振るときの平均的な早さは  秒程度。加速・減速を考慮して、その2倍を最高速度とする
        // 顔のふり具合を、中央(0.0)から、左右は(+-1.0)とする
        var FaceParamMaxV = 40.0f / 10.0f; // 7.5秒間に40分移動（5.3/sc)
        var MaxV = FaceParamMaxV * 1.0f / FrameRate; // 1frameあたりに変化できる速度の上限

        if (_lastTimeSeconds == 0.0f)
        {
            _lastTimeSeconds = _userTimeSeconds;

            return;
        }

        var deltaTimeWeight = (_userTimeSeconds - _lastTimeSeconds) * FrameRate;
        _lastTimeSeconds = _userTimeSeconds;

        // 最高速度になるまでの時間を
        var TimeToMaxSpeed = 0.15f;
        var FrameToMaxSpeed = TimeToMaxSpeed * FrameRate; // sec * frame/sec
        var MaxA = deltaTimeWeight * MaxV / FrameToMaxSpeed; // 1frameあたりの加速度

        // 目指す向きは、(dx, dy)方向のベクトルとなる
        var dx = _faceTargetX - FaceX;
        var dy = _faceTargetY - FaceY;

        if (MathF.Abs(dx) <= Epsilon && MathF.Abs(dy) <= Epsilon)
        {
            return; // 変化なし
        }

        // 速度の最大よりも大きい場合は、速度を落とす
        var d = MathF.Sqrt(dx * dx + dy * dy);

        // 進行方向の最大速度ベクトル
        var vx = MaxV * dx / d;
        var vy = MaxV * dy / d;

        // 現在の速度から、新規速度への変化（加速度）を求める
        var ax = vx - _faceVX;
        var ay = vy - _faceVY;

        var a = MathF.Sqrt(ax * ax + ay * ay);

        // 加速のとき
        if (a < -MaxA || a > MaxA)
        {
            ax *= MaxA / a;
            ay *= MaxA / a;
        }

        // 加速度を元の速度に足して、新速度とする
        _faceVX += ax;
        _faceVY += ay;

        // 目的の方向に近づいたとき、滑らかに減速するための処理
        // 設定された加速度で止まることのできる距離と速度の関係から
        // 現在とりうる最高速度を計算し、それ以上のときは速度を落とす
        // ※本来、人間は筋力で力（加速度）を調整できるため、より自由度が高いが、簡単な処理ですませている
        {
            // 加速度、速度、距離の関係式。
            //            2  6           2               3
            //      sqrt(a  t  + 16 a h t  - 8 a h) - a t
            // v = --------------------------------------
            //                    2
            //                 4 t  - 2
            // (t=1)
            //  時刻tは、あらかじめ加速度、速度を1/60(フレームレート、単位なし)で
            //  考えているので、t＝１として消してよい（※未検証）

            var maxV = 0.5f * (MathF.Sqrt(MaxA * MaxA + 16.0f * MaxA * d - 8.0f * MaxA * d) - MaxA);
            var curV = MathF.Sqrt(_faceVX * _faceVX + _faceVY * _faceVY);

            if (curV > maxV)
            {
                // 現在の速度 > 最高速度のとき、最高速度まで減速
                _faceVX *= maxV / curV;
                _faceVY *= maxV / curV;
            }
        }

        FaceX += _faceVX;
        FaceY += _faceVY;
    }

    /// <summary>
    ///     顔の向きの目標値を設定する。
    /// </summary>
    /// <param name="x">X軸の顔の向きの値(-1.0 - 1.0)</param>
    /// <param name="y">Y軸の顔の向きの値(-1.0 - 1.0)</param>
    public void Set(float x, float y)
    {
        _faceTargetX = x;
        _faceTargetY = y;
    }
}
