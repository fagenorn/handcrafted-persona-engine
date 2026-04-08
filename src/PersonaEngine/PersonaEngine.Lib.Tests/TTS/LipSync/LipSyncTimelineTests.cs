using PersonaEngine.Lib.TTS.Synthesis.LipSync;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.LipSync;

public class LipSyncTimelineTests
{
    private static LipSyncFrame MakeFrame(
        double timestamp,
        float mouthOpenY = 0f,
        float? eyeLOpen = null
    ) =>
        new LipSyncFrame
        {
            Timestamp = timestamp,
            MouthOpenY = mouthOpenY,
            EyeLOpen = eyeLOpen,
        };

    [Fact]
    public void GetFrameAtTime_EmptyTimeline_ReturnsNeutral()
    {
        var timeline = LipSyncTimeline.Empty;

        var result = timeline.GetFrameAtTime(1.0);

        Assert.Equal(LipSyncFrame.Neutral.MouthOpenY, result.MouthOpenY);
        Assert.Equal(LipSyncFrame.Neutral.JawOpen, result.JawOpen);
        Assert.Equal(LipSyncFrame.Neutral.Timestamp, result.Timestamp);
    }

    [Fact]
    public void GetFrameAtTime_BeforeFirstFrame_ReturnsFirstFrame()
    {
        var frames = new[] { MakeFrame(1.0, mouthOpenY: 0.5f), MakeFrame(2.0, mouthOpenY: 1.0f) };
        var timeline = new LipSyncTimeline(frames, 2f);

        var result = timeline.GetFrameAtTime(0.0);

        Assert.Equal(0.5f, result.MouthOpenY);
        Assert.Equal(1.0, result.Timestamp);
    }

    [Fact]
    public void GetFrameAtTime_AfterLastFrame_ReturnsLastFrame()
    {
        var frames = new[] { MakeFrame(0.0, mouthOpenY: 0.0f), MakeFrame(1.0, mouthOpenY: 0.8f) };
        var timeline = new LipSyncTimeline(frames, 1f);

        var result = timeline.GetFrameAtTime(5.0);

        Assert.Equal(0.8f, result.MouthOpenY);
        Assert.Equal(1.0, result.Timestamp);
    }

    [Fact]
    public void GetFrameAtTime_Midway_InterpolatesLinearly()
    {
        var frames = new[] { MakeFrame(0.0, mouthOpenY: 0.0f), MakeFrame(1.0, mouthOpenY: 1.0f) };
        var timeline = new LipSyncTimeline(frames, 1f);

        var result = timeline.GetFrameAtTime(0.5);

        Assert.Equal(0.5f, result.MouthOpenY, precision: 5);
    }

    [Fact]
    public void GetFrameAtTime_NullableEyeParams_PreservedWhenBothPresent()
    {
        var frames = new[]
        {
            MakeFrame(0.0, mouthOpenY: 0f, eyeLOpen: 0.2f),
            MakeFrame(1.0, mouthOpenY: 0f, eyeLOpen: 0.8f),
        };
        var timeline = new LipSyncTimeline(frames, 1f);

        var result = timeline.GetFrameAtTime(0.5);

        Assert.NotNull(result.EyeLOpen);
        Assert.Equal(0.5f, result.EyeLOpen!.Value, precision: 5);
    }

    [Fact]
    public void GetFrameAtTime_NullableEyeParams_NullWhenEitherMissing()
    {
        var frames = new[]
        {
            MakeFrame(0.0, mouthOpenY: 0f, eyeLOpen: 0.5f),
            MakeFrame(1.0, mouthOpenY: 0f, eyeLOpen: null),
        };
        var timeline = new LipSyncTimeline(frames, 1f);

        var result = timeline.GetFrameAtTime(0.5);

        Assert.Null(result.EyeLOpen);
    }

    [Fact]
    public void GetFrameAtTime_BinarySearch_WorksWith100Frames()
    {
        const int frameCount = 100;
        var frames = Enumerable
            .Range(0, frameCount)
            .Select(i => MakeFrame(i * 0.1, mouthOpenY: i * 0.01f))
            .ToArray();

        var timeline = new LipSyncTimeline(frames, frameCount * 0.1f);

        // Query each exact frame timestamp and one midpoint
        for (var i = 0; i < frameCount; i++)
        {
            var result = timeline.GetFrameAtTime(i * 0.1);
            Assert.Equal((float)(i * 0.01), result.MouthOpenY, precision: 4);
        }

        // Midpoint between frame 50 and 51: MouthOpenY should be ~0.505
        var mid = timeline.GetFrameAtTime(50 * 0.1 + 0.05);
        Assert.Equal(0.505f, mid.MouthOpenY, precision: 3);
    }
}
