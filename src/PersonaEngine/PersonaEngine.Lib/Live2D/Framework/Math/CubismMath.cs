using System.Numerics;

namespace PersonaEngine.Lib.Live2D.Framework.Math;

/// <summary>
///     数値計算などに使用するユーティリティクラス
/// </summary>
public static class CubismMath
{
    public const float Pi = 3.1415926535897932384626433832795f;

    public const float Epsilon = 0.00001f;

    /// <summary>
    ///     第一引数の値を最小値と最大値の範囲に収めた値を返す
    /// </summary>
    /// <param name="value">収められる値</param>
    /// <param name="min">範囲の最小値</param>
    /// <param name="max">範囲の最大値</param>
    /// <returns>最小値と最大値の範囲に収めた値</returns>
    public static float RangeF(float value, float min, float max)
    {
        if (value < min)
        {
            value = min;
        }
        else if (value > max)
        {
            value = max;
        }

        return value;
    }

    /// <summary>
    ///     イージング処理されたサインを求める
    ///     フェードイン・アウト時のイージングに利用できる
    /// </summary>
    /// <param name="value">イージングを行う値</param>
    /// <returns>イージング処理されたサイン値</returns>
    public static float GetEasingSine(float value)
    {
        if (value < 0.0f)
        {
            return 0.0f;
        }

        if (value > 1.0f)
        {
            return 1.0f;
        }

        return 0.5f - 0.5f * MathF.Cos(value * Pi);
    }

    /// <summary>
    ///     大きい方の値を返す。
    /// </summary>
    /// <param name="l">左辺の値</param>
    /// <param name="r">右辺の値</param>
    /// <returns>大きい方の値</returns>
    public static float Max(float l, float r)
    {
        return l > r ? l : r;
    }

    /// <summary>
    ///     小さい方の値を返す。
    /// </summary>
    /// <param name="l">左辺の値</param>
    /// <param name="r">右辺の値</param>
    /// <returns>小さい方の値</returns>
    public static float Min(float l, float r)
    {
        return l > r ? r : l;
    }

    /// <summary>
    ///     角度値をラジアン値に変換します。
    /// </summary>
    /// <param name="degrees">角度値</param>
    /// <returns>角度値から変換したラジアン値</returns>
    public static float DegreesToRadian(float degrees)
    {
        return degrees / 180.0f * Pi;
    }

    /// <summary>
    ///     ラジアン値を角度値に変換します。
    /// </summary>
    /// <param name="radian">ラジアン値</param>
    /// <returns>ラジアン値から変換した角度値</returns>
    public static float RadianToDegrees(float radian)
    {
        return radian * 180.0f / Pi;
    }

    /// <summary>
    ///     2つのベクトルからラジアン値を求めます。
    /// </summary>
    /// <param name="from">始点ベクトル</param>
    /// <param name="to">終点ベクトル</param>
    /// <returns>ラジアン値から求めた方向ベクトル</returns>
    public static float DirectionToRadian(Vector2 from, Vector2 to)
    {
        float q1;
        float q2;
        float ret;

        q1 = MathF.Atan2(to.Y, to.X);
        q2 = MathF.Atan2(from.Y, from.X);

        ret = q1 - q2;

        while (ret < -Pi)
        {
            ret += Pi * 2.0f;
        }

        while (ret > Pi)
        {
            ret -= Pi * 2.0f;
        }

        return ret;
    }

    /// <summary>
    ///     2つのベクトルから角度値を求めます。
    /// </summary>
    /// <param name="from">始点ベクトル</param>
    /// <param name="to">終点ベクトル</param>
    /// <returns>角度値から求めた方向ベクトル</returns>
    public static float DirectionToDegrees(Vector2 from, Vector2 to)
    {
        float radian;
        float degree;

        radian = DirectionToRadian(from, to);
        degree = RadianToDegrees(radian);

        if (to.X - from.X > 0.0f)
        {
            degree = -degree;
        }

        return degree;
    }

    /// <summary>
    ///     ラジアン値を方向ベクトルに変換します。
    /// </summary>
    /// <param name="totalAngle">ラジアン値</param>
    /// <returns>ラジアン値から変換した方向ベクトル</returns>
    public static Vector2 RadianToDirection(float totalAngle)
    {
        return new Vector2(MathF.Sin(totalAngle), MathF.Cos(totalAngle));
    }

    /// <summary>
    ///     三次方程式の三次項の係数が0になったときに補欠的に二次方程式の解をもとめる。
    ///     a * x^2 + b * x + c = 0
    /// </summary>
    /// <param name="a">二次項の係数値</param>
    /// <param name="b">一次項の係数値</param>
    /// <param name="c">定数項の値</param>
    /// <returns>二次方程式の解</returns>
    public static float QuadraticEquation(float a, float b, float c)
    {
        if (MathF.Abs(a) < Epsilon)
        {
            if (MathF.Abs(b) < Epsilon)
            {
                return -c;
            }

            return -c / b;
        }

        return -(b + MathF.Sqrt(b * b - 4.0f * a * c)) / (2.0f * a);
    }

    /// <summary>
    ///     カルダノの公式によってベジェのt値に該当する３次方程式の解を求める。
    ///     重解になったときには0.0～1.0の値になる解を返す。
    ///     a * x^3 + b * x^2 + c * x + d = 0
    /// </summary>
    /// <param name="a">三次項の係数値</param>
    /// <param name="b">二次項の係数値</param>
    /// <param name="c">一次項の係数値</param>
    /// <param name="d">定数項の値</param>
    /// <returns>0.0～1.0の間にある解</returns>
    public static float CardanoAlgorithmForBezier(float a, float b, float c, float d)
    {
        if (MathF.Abs(a) < Epsilon)
        {
            return RangeF(QuadraticEquation(b, c, d), 0.0f, 1.0f);
        }

        var ba = b / a;
        var ca = c / a;
        var da = d / a;

        var p = (3.0f * ca - ba * ba) / 3.0f;
        var p3 = p / 3.0f;
        var q = (2.0f * ba * ba * ba - 9.0f * ba * ca + 27.0f * da) / 27.0f;
        var q2 = q / 2.0f;
        var discriminant = q2 * q2 + p3 * p3 * p3;

        var center = 0.5f;
        var threshold = center + 0.01f;

        float root1;
        float u1;

        if (discriminant < 0.0f)
        {
            var mp3 = -p / 3.0f;
            var mp33 = mp3 * mp3 * mp3;
            var r = MathF.Sqrt(mp33);
            var t = -q / (2.0f * r);
            var cosphi = RangeF(t, -1.0f, 1.0f);
            var phi = MathF.Acos(cosphi);
            var crtr = MathF.Cbrt(r);
            var t1 = 2.0f * crtr;

            root1 = t1 * MathF.Cos(phi / 3.0f) - ba / 3.0f;
            if (MathF.Abs(root1 - center) < threshold)
            {
                return RangeF(root1, 0.0f, 1.0f);
            }

            var root2 = t1 * MathF.Cos((phi + 2.0f * Pi) / 3.0f) - ba / 3.0f;
            if (MathF.Abs(root2 - center) < threshold)
            {
                return RangeF(root2, 0.0f, 1.0f);
            }

            var root3 = t1 * MathF.Cos((phi + 4.0f * Pi) / 3.0f) - ba / 3.0f;

            return RangeF(root3, 0.0f, 1.0f);
        }

        if (discriminant == 0.0f)
        {
            if (q2 < 0.0f)
            {
                u1 = MathF.Cbrt(-q2);
            }
            else
            {
                u1 = -MathF.Cbrt(q2);
            }

            root1 = 2.0f * u1 - ba / 3.0f;
            if (MathF.Abs(root1 - center) < threshold)
            {
                return RangeF(root1, 0.0f, 1.0f);
            }

            var root2 = -u1 - ba / 3.0f;

            return RangeF(root2, 0.0f, 1.0f);
        }

        var sd = MathF.Sqrt(discriminant);
        u1 = MathF.Cbrt(sd - q2);
        var v1 = MathF.Cbrt(sd + q2);
        root1 = u1 - v1 - ba / 3.0f;

        return RangeF(root1, 0.0f, 1.0f);
    }

    /// <summary>
    ///     値を範囲内に納めて返す
    /// </summary>
    /// <param name="val">範囲内か確認する値</param>
    /// <param name="min">最小値</param>
    /// <param name="max">最大値</param>
    /// <returns>範囲内に収まった値</returns>
    public static int Clamp(int val, int min, int max)
    {
        if (val < min)
        {
            return min;
        }

        if (max < val)
        {
            return max;
        }

        return val;
    }

    /// <summary>
    ///     値を範囲内に納めて返す
    /// </summary>
    /// <param name="val">範囲内か確認する値</param>
    /// <param name="min">最小値</param>
    /// <param name="max">最大値</param>
    /// <returns>範囲内に収まった値</returns>
    public static float ClampF(float val, float min, float max)
    {
        if (val < min)
        {
            return min;
        }

        if (max < val)
        {
            return max;
        }

        return val;
    }

    /// <summary>
    ///     浮動小数点の余りを求める。
    /// </summary>
    /// <param name="dividend">被除数（割られる値）</param>
    /// <param name="divisor">除数（割る値）</param>
    /// <returns>余り</returns>
    public static float ModF(float dividend, float divisor)
    {
        if (
            !float.IsFinite(dividend)
            || divisor == 0
            || float.IsNaN(dividend)
            || float.IsNaN(divisor)
        )
        {
            CubismLog.Warning("dividend: %f, divisor: %f ModF() returns 'NaN'.", dividend, divisor);

            return float.NaN;
        }

        // 絶対値に変換する。
        var absDividend = MathF.Abs(dividend);
        var absDivisor = MathF.Abs(divisor);

        // 絶対値で割り算する。
        var result = absDividend - MathF.Floor(absDividend / absDivisor) * absDivisor;

        // 符号を被除数のものに指定する。
        return MathF.CopySign(result, dividend);
    }
}
