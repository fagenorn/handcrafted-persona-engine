namespace PersonaEngine.Lib.TTS.Synthesis.LipSync.Audio2Face;

/// <summary>
///     Per-parameter exponential moving average smoother matching VBridger
///     AdvancedARKit_V3.0 smoothing factors. Applied to LipSyncFrame data
///     after ARKit-to-Live2D mapping, before timeline construction.
/// </summary>
public sealed class ParamSmoother
{
    // VBridger smoothing factors per parameter
    private const float SmJawOpen = 0.1f;
    private const float SmMouthOpenY = 0.15f;
    private const float SmMouthForm = 0.1f;
    private const float SmMouthFunnel = 0.1f;
    private const float SmMouthPressLipOpen = 0.1f;
    private const float SmMouthPuckerWiden = 0.1f;
    private const float SmMouthX = 0.1f;
    private const float SmMouthShrug = 0.1f;
    private const float SmCheekPuffC = 0.0f; // no smoothing
    private const float SmEyeOpen = 0.1f;
    private const float SmEyeSquint = 0.0f; // no smoothing
    private const float SmBrowY = 0.4f;
    private const float SmBrowInnerUp = 0.32f;
    private const float SmCheek = 0.0f; // no smoothing

    private LipSyncFrame _prev;
    private bool _hasPrev;

    /// <summary>
    ///     Applies per-parameter EMA smoothing to a frame.
    ///     First frame passes through unchanged.
    /// </summary>
    public LipSyncFrame Smooth(in LipSyncFrame input)
    {
        if (!_hasPrev)
        {
            _hasPrev = true;
            _prev = input;
            return input;
        }

        var result = new LipSyncFrame
        {
            JawOpen = Ema(_prev.JawOpen, input.JawOpen, SmJawOpen),
            MouthOpenY = Ema(_prev.MouthOpenY, input.MouthOpenY, SmMouthOpenY),
            MouthForm = Ema(_prev.MouthForm, input.MouthForm, SmMouthForm),
            MouthFunnel = Ema(_prev.MouthFunnel, input.MouthFunnel, SmMouthFunnel),
            MouthPressLipOpen = Ema(
                _prev.MouthPressLipOpen,
                input.MouthPressLipOpen,
                SmMouthPressLipOpen
            ),
            MouthPuckerWiden = Ema(
                _prev.MouthPuckerWiden,
                input.MouthPuckerWiden,
                SmMouthPuckerWiden
            ),
            MouthX = Ema(_prev.MouthX, input.MouthX, SmMouthX),
            MouthShrug = Ema(_prev.MouthShrug, input.MouthShrug, SmMouthShrug),
            CheekPuffC = input.CheekPuffC, // SmCheekPuffC = 0 → no smoothing
            EyeLOpen = EmaNullable(_prev.EyeLOpen, input.EyeLOpen, SmEyeOpen),
            EyeROpen = EmaNullable(_prev.EyeROpen, input.EyeROpen, SmEyeOpen),
            EyeSquintL = EmaNullable(_prev.EyeSquintL, input.EyeSquintL, SmEyeSquint),
            EyeSquintR = EmaNullable(_prev.EyeSquintR, input.EyeSquintR, SmEyeSquint),
            BrowLY = EmaNullable(_prev.BrowLY, input.BrowLY, SmBrowY),
            BrowRY = EmaNullable(_prev.BrowRY, input.BrowRY, SmBrowY),
            BrowInnerUp = EmaNullable(_prev.BrowInnerUp, input.BrowInnerUp, SmBrowInnerUp),
            Cheek = EmaNullable(_prev.Cheek, input.Cheek, SmCheek),
            EyeBallX = EmaNullable(_prev.EyeBallX, input.EyeBallX, SmEyeOpen),
            EyeBallY = EmaNullable(_prev.EyeBallY, input.EyeBallY, SmEyeOpen),
            Timestamp = input.Timestamp,
        };

        _prev = result;
        return result;
    }

    /// <summary>
    ///     Resets the smoother state. Next frame will pass through unchanged.
    /// </summary>
    public void Reset()
    {
        _prev = default;
        _hasPrev = false;
    }

    public (LipSyncFrame Prev, bool HasPrev) Save() => (_prev, _hasPrev);

    public void Restore((LipSyncFrame Prev, bool HasPrev) state)
    {
        _prev = state.Prev;
        _hasPrev = state.HasPrev;
    }

    /// <summary>EMA: output = prev * factor + input * (1 - factor)</summary>
    private static float Ema(float prev, float input, float factor)
    {
        return prev * factor + input * (1f - factor);
    }

    private static float? EmaNullable(float? prev, float? input, float factor)
    {
        if (!input.HasValue)
            return input;
        if (!prev.HasValue || factor <= 0f)
            return input;
        return Ema(prev.Value, input.Value, factor);
    }
}
