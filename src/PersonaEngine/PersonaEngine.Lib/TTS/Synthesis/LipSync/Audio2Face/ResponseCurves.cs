namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;

public static class ResponseCurves
{
    /// <summary>
    /// Ease-in curve: steep at start (tangent 2.0), flat at end.
    /// Hermite spline: f(t) = t * (2 - t). Used for JawOpen/MouthOpen.
    /// </summary>
    public static float EaseIn(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * (2f - t);
    }

    /// <summary>
    /// 3-key center-weighted Hermite spline: flat at extremes, steep through center.
    /// Keys: (lo, lo, tangent=0), (0, 0, tangent=2), (hi, hi, tangent=0).
    /// Used for MouthPressLipOpen, EyeBallY.
    /// </summary>
    public static float CenterWeighted(float t, float lo, float hi)
    {
        t = Math.Clamp(t, lo, hi);

        if (t <= 0f)
        {
            // Lower segment: lo..0
            var span = -lo;
            if (span < 1e-6f)
                return t;
            var s = (t - lo) / span; // 0..1
            // Hermite: p0=lo, p1=0, m0=0, m1=2*span
            var s2 = s * s;
            var s3 = s2 * s;
            var h00 = 2f * s3 - 3f * s2 + 1f;
            var h10 = s3 - 2f * s2 + s;
            var h01 = -2f * s3 + 3f * s2;
            var h11 = s3 - s2;
            return h00 * lo + h10 * 0f + h01 * 0f + h11 * (2f * span);
        }
        else
        {
            // Upper segment: 0..hi
            var span = hi;
            if (span < 1e-6f)
                return t;
            var s = t / span; // 0..1
            var s2 = s * s;
            var s3 = s2 * s;
            var h00 = 2f * s3 - 3f * s2 + 1f;
            var h10 = s3 - 2f * s2 + s;
            var h01 = -2f * s3 + 3f * s2;
            var h11 = s3 - s2;
            return h00 * 0f + h10 * (2f * span) + h01 * hi + h11 * 0f;
        }
    }
}
