namespace PersonaEngine.Lib.TTS.Synthesis.LipSync;

/// <summary>
///     A single frame of lip-sync data at a specific point in time.
///     Nullable eye/brow params are optional — not all engines produce them.
/// </summary>
public struct LipSyncFrame
{
    // Core mouth parameters
    public float MouthOpenY;
    public float JawOpen;
    public float MouthForm;
    public float MouthShrug;
    public float MouthFunnel;
    public float MouthPuckerWiden;
    public float MouthPressLipOpen;
    public float MouthX;
    public float CheekPuffC;

    // Optional facial parameters
    public float? EyeLOpen;
    public float? EyeROpen;
    public float? BrowLY;
    public float? BrowRY;

    public double Timestamp;

    /// <summary>
    ///     The neutral / default frame (all zeroes).
    /// </summary>
    public static readonly LipSyncFrame Neutral = default;

    /// <summary>
    ///     Linearly interpolates all fields between <paramref name="a" /> and <paramref name="b" /> by factor <paramref name="t" />.
    ///     Nullable fields return null if either input is null.
    /// </summary>
    public static LipSyncFrame Lerp(in LipSyncFrame a, in LipSyncFrame b, float t)
    {
        return new LipSyncFrame
        {
            MouthOpenY = a.MouthOpenY + (b.MouthOpenY - a.MouthOpenY) * t,
            JawOpen = a.JawOpen + (b.JawOpen - a.JawOpen) * t,
            MouthForm = a.MouthForm + (b.MouthForm - a.MouthForm) * t,
            MouthShrug = a.MouthShrug + (b.MouthShrug - a.MouthShrug) * t,
            MouthFunnel = a.MouthFunnel + (b.MouthFunnel - a.MouthFunnel) * t,
            MouthPuckerWiden = a.MouthPuckerWiden + (b.MouthPuckerWiden - a.MouthPuckerWiden) * t,
            MouthPressLipOpen =
                a.MouthPressLipOpen + (b.MouthPressLipOpen - a.MouthPressLipOpen) * t,
            MouthX = a.MouthX + (b.MouthX - a.MouthX) * t,
            CheekPuffC = a.CheekPuffC + (b.CheekPuffC - a.CheekPuffC) * t,
            EyeLOpen =
                a.EyeLOpen.HasValue && b.EyeLOpen.HasValue
                    ? a.EyeLOpen.Value + (b.EyeLOpen.Value - a.EyeLOpen.Value) * t
                    : null,
            EyeROpen =
                a.EyeROpen.HasValue && b.EyeROpen.HasValue
                    ? a.EyeROpen.Value + (b.EyeROpen.Value - a.EyeROpen.Value) * t
                    : null,
            BrowLY =
                a.BrowLY.HasValue && b.BrowLY.HasValue
                    ? a.BrowLY.Value + (b.BrowLY.Value - a.BrowLY.Value) * t
                    : null,
            BrowRY =
                a.BrowRY.HasValue && b.BrowRY.HasValue
                    ? a.BrowRY.Value + (b.BrowRY.Value - a.BrowRY.Value) * t
                    : null,
            Timestamp = a.Timestamp + (b.Timestamp - a.Timestamp) * t,
        };
    }
}

/// <summary>
///     An ordered sequence of <see cref="LipSyncFrame" />s covering a single audio segment.
///     Frames must be sorted ascending by <see cref="LipSyncFrame.Timestamp" />.
/// </summary>
public sealed class LipSyncTimeline
{
    /// <summary>
    ///     An empty timeline with no frames and zero duration.
    /// </summary>
    public static readonly LipSyncTimeline Empty = new(Array.Empty<LipSyncFrame>(), 0f);

    public LipSyncTimeline(LipSyncFrame[] frames, float duration)
    {
        Frames = frames;
        Duration = duration;
    }

    /// <summary>
    ///     Frames sorted ascending by <see cref="LipSyncFrame.Timestamp" />.
    /// </summary>
    public LipSyncFrame[] Frames { get; }

    /// <summary>
    ///     Total duration of the timeline in seconds.
    /// </summary>
    public float Duration { get; }

    /// <summary>
    ///     Returns the interpolated <see cref="LipSyncFrame" /> at the given <paramref name="time" /> (in seconds).
    ///     Returns the neutral frame when there are no frames.
    ///     Clamps to first/last frame when out of range.
    /// </summary>
    public LipSyncFrame GetFrameAtTime(double time)
    {
        if (Frames.Length == 0)
        {
            return LipSyncFrame.Neutral;
        }

        if (time <= Frames[0].Timestamp)
        {
            return Frames[0];
        }

        if (time >= Frames[^1].Timestamp)
        {
            return Frames[^1];
        }

        // Binary search for the first frame with Timestamp > time
        var lo = 0;
        var hi = Frames.Length - 1;

        while (lo < hi - 1)
        {
            var mid = (lo + hi) / 2;
            if (Frames[mid].Timestamp <= time)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        // lo is the frame just before time, hi is the frame just after
        ref readonly var frameA = ref Frames[lo];
        ref readonly var frameB = ref Frames[hi];

        var span = frameB.Timestamp - frameA.Timestamp;
        var t = span > 0.0 ? (float)((time - frameA.Timestamp) / span) : 0f;

        return LipSyncFrame.Lerp(in frameA, in frameB, t);
    }
}
