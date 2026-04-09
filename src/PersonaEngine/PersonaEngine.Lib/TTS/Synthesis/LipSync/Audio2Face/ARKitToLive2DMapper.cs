namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;

/// <summary>
///     Converts 52 ARKit blendshape weights into a <see cref="LipSyncFrame" />
///     using VBridger AdvancedARKit_V3.0 formulas.
/// </summary>
public static class ARKitToLive2DMapper
{
    // ── ARKit blendshape indices (52 total, 28+ used) ──

    private const int EyeBlinkLeft = 0;

    // 1-4 unused
    private const int EyeSquintLeft = 5;
    private const int EyeWideLeft = 6;
    private const int EyeBlinkRight = 7;

    // 8-11 unused
    private const int EyeSquintRight = 12;
    private const int EyeWideRight = 13;
    private const int JawForward = 14;

    // 15-16 unused
    private const int JawOpen = 17;
    private const int MouthClose = 18;
    private const int MouthFunnel = 19;
    private const int MouthPucker = 20;
    private const int MouthLeft = 21;
    private const int MouthRight = 22;
    private const int MouthSmileLeft = 23;
    private const int MouthSmileRight = 24;
    private const int MouthFrownLeft = 25;
    private const int MouthFrownRight = 26;
    private const int MouthDimpleLeft = 27;
    private const int MouthDimpleRight = 28;
    private const int MouthStretchLeft = 29;
    private const int MouthStretchRight = 30;
    private const int MouthRollLower = 31;
    private const int MouthRollUpper = 32;
    private const int MouthShrugLower = 33;
    private const int MouthShrugUpper = 34;
    private const int MouthPressLeft = 35;
    private const int MouthPressRight = 36;
    private const int MouthLowerDownLeft = 37;
    private const int MouthLowerDownRight = 38;
    private const int MouthUpperUpLeft = 39;
    private const int MouthUpperUpRight = 40;
    private const int BrowDownLeft = 41;
    private const int BrowDownRight = 42;
    private const int BrowInnerUp = 43;
    private const int BrowOuterUpLeft = 44;
    private const int BrowOuterUpRight = 45;
    private const int CheekPuff = 46;
    private const int CheekSquintLeft = 47;
    private const int CheekSquintRight = 48;
    private const int NoseSneerLeft = 49;
    private const int NoseSneerRight = 50;

    private const int ExpectedWeightCount = 52;

    /// <summary>
    ///     Maps a 52-element ARKit blendshape weight array to a <see cref="LipSyncFrame" />
    ///     using VBridger AdvancedARKit_V3.0 formulas.
    /// </summary>
    /// <param name="weights">
    ///     52 blendshape weights in standard ARKit order, each in [0, 1].
    /// </param>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="weights" /> does not contain exactly 52 elements.
    /// </exception>
    public static LipSyncFrame Map(ReadOnlySpan<float> weights)
    {
        if (weights.Length != ExpectedWeightCount)
        {
            throw new ArgumentException(
                $"Expected {ExpectedWeightCount} ARKit blendshape weights, got {weights.Length}.",
                nameof(weights)
            );
        }

        // ── Read inputs ──
        var jawOpen = weights[JawOpen];
        var mouthClose = weights[MouthClose];
        var mouthFunnel = weights[MouthFunnel];
        var mouthPucker = weights[MouthPucker];
        var mouthLeft = weights[MouthLeft];
        var mouthRight = weights[MouthRight];
        var smileL = weights[MouthSmileLeft];
        var smileR = weights[MouthSmileRight];
        var frownL = weights[MouthFrownLeft];
        var frownR = weights[MouthFrownRight];
        var dimpleL = weights[MouthDimpleLeft];
        var dimpleR = weights[MouthDimpleRight];
        var rollLower = weights[MouthRollLower];
        var rollUpper = weights[MouthRollUpper];
        var shrugLower = weights[MouthShrugLower];
        var shrugUpper = weights[MouthShrugUpper];
        var pressL = weights[MouthPressLeft];
        var pressR = weights[MouthPressRight];
        var lowerDownL = weights[MouthLowerDownLeft];
        var lowerDownR = weights[MouthLowerDownRight];
        var upperUpL = weights[MouthUpperUpLeft];
        var upperUpR = weights[MouthUpperUpRight];
        var cheekPuff = weights[CheekPuff];

        // ── JawOpen: ease-in curve ──
        var jawOpenOut = ResponseCurves.EaseIn(Math.Clamp(jawOpen, 0f, 1f));

        // ── MouthOpenY: VBridger formula with ease-in ──
        var mouthOpenRaw = Math.Clamp(
            (jawOpen - mouthClose) - (rollUpper + rollLower) * 0.2f + mouthFunnel * 0.2f,
            0f,
            1f
        );
        var mouthOpenY = ResponseCurves.EaseIn(mouthOpenRaw);

        // ── MouthForm: VBridger compound smile-frown axis ──
        var dimpleAvg = (dimpleL + dimpleR) / 2f;
        var mouthForm = Math.Clamp(
            (2f - frownL - frownR - mouthPucker + smileR + smileL + dimpleAvg) / 4f,
            -1f,
            1f
        );

        // ── MouthFunnel: raw funnel minus jaw artifact ──
        var mouthFunnelOut = Math.Clamp(mouthFunnel - jawOpen * 0.2f, 0f, 1f);

        // ── MouthPressLipOpen: center-weighted Hermite spline ──
        var pressRaw = Math.Clamp(
            (upperUpR + upperUpL + lowerDownR + lowerDownL) / 1.8f - (rollLower + rollUpper),
            -1.3f,
            1.3f
        );
        var mouthPressLipOpen = ResponseCurves.CenterWeighted(pressRaw, -1.3f, 1.3f);

        // ── MouthPuckerWiden: spread-vs-pucker ──
        var mouthPuckerWiden = Math.Clamp((dimpleR + dimpleL) * 2f - mouthPucker, -1f, 1f);

        // ── MouthX: lateral shift + asymmetric smile ──
        var mouthX = Math.Clamp((mouthLeft - mouthRight) + (smileL - smileR), -1f, 1f);

        // ── MouthShrug: chin raise + lip compression ──
        var mouthShrug = Math.Clamp((shrugUpper + shrugLower + pressR + pressL) / 4f, 0f, 1f);

        // ── CheekPuff: direct passthrough ──
        var cheekPuffC = Math.Clamp(cheekPuff, 0f, 1f);

        // ── Eyes: Aria [0,2] range (default 1.0 = normal open) ──
        var eyeLOpen = Math.Clamp(
            (0.5f + weights[EyeBlinkLeft] * -0.8f + weights[EyeWideLeft] * 0.8f) * 2f,
            0f,
            2f
        );
        var eyeROpen = Math.Clamp(
            (0.5f + weights[EyeBlinkRight] * -0.8f + weights[EyeWideRight] * 0.8f) * 2f,
            0f,
            2f
        );

        // ── Eye squint: direct passthrough ──
        var eyeSquintL = Math.Clamp(weights[EyeSquintLeft], 0f, 1f);
        var eyeSquintR = Math.Clamp(weights[EyeSquintRight], 0f, 1f);

        // ── Brows: with mouth-shift coupling ──
        var browLY = Math.Clamp(
            (weights[BrowOuterUpLeft] - weights[BrowDownLeft]) + (mouthRight - mouthLeft) / 8f,
            -1f,
            1f
        );
        var browRY = Math.Clamp(
            (weights[BrowOuterUpRight] - weights[BrowDownRight]) + (mouthLeft - mouthRight) / 8f,
            -1f,
            1f
        );

        // ── BrowInnerUp: direct passthrough ──
        var browInnerUpOut = Math.Clamp(weights[BrowInnerUp], 0f, 1f);

        // ── Cheek: average of cheek squint ──
        var cheek = Math.Clamp((weights[CheekSquintLeft] + weights[CheekSquintRight]) / 2f, 0f, 1f);

        return new LipSyncFrame
        {
            MouthOpenY = mouthOpenY,
            JawOpen = jawOpenOut,
            MouthForm = mouthForm,
            MouthShrug = mouthShrug,
            MouthFunnel = mouthFunnelOut,
            MouthPuckerWiden = mouthPuckerWiden,
            MouthPressLipOpen = mouthPressLipOpen,
            MouthX = mouthX,
            CheekPuffC = cheekPuffC,
            EyeLOpen = eyeLOpen,
            EyeROpen = eyeROpen,
            EyeSquintL = eyeSquintL,
            EyeSquintR = eyeSquintR,
            BrowLY = browLY,
            BrowRY = browRY,
            BrowInnerUp = browInnerUpOut,
            Cheek = cheek,
        };
    }
}
