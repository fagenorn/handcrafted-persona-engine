using PersonaEngine.Lib.Audio;
using Xunit;

namespace PersonaEngine.Lib.Tests.Audio;

public class MicrophoneAmplitudeProviderTests
{
    [Fact]
    public void CurrentAmplitude_Initially_IsZero()
    {
        var mic = new FakeMicrophone();
        var provider = new MicrophoneAmplitudeProvider(mic);
        Assert.Equal(0f, provider.CurrentAmplitude);
    }

    [Fact]
    public void History_Initially_AllZero()
    {
        var mic = new FakeMicrophone();
        var provider = new MicrophoneAmplitudeProvider(mic);
        foreach (var v in provider.History)
            Assert.Equal(0f, v);
    }

    [Fact]
    public void OnSamplesAvailable_UpdatesAmplitude_TowardRms()
    {
        var mic = new FakeMicrophone();
        var provider = new MicrophoneAmplitudeProvider(mic);

        // Constant-amplitude buffer: RMS of all 0.5 is 0.5
        var samples = new float[320];
        Array.Fill(samples, 0.5f);
        mic.Raise(samples, 16000);

        // Provider smooths toward the target — after one event expect > 0 but ≤ 0.5
        Assert.InRange(provider.CurrentAmplitude, 0f, 0.5f + 1e-4f);
        Assert.True(provider.CurrentAmplitude > 0f);
    }

    [Fact]
    public void OnSamplesAvailable_AdvancesHistoryHead()
    {
        var mic = new FakeMicrophone();
        var provider = new MicrophoneAmplitudeProvider(mic);
        var startHead = provider.HistoryHead;

        var samples = new float[320];
        Array.Fill(samples, 0.1f);
        mic.Raise(samples, 16000);

        Assert.NotEqual(startHead, provider.HistoryHead);
    }

    [Fact]
    public void Dispose_UnsubscribesFromEvent()
    {
        var mic = new FakeMicrophone();
        var provider = new MicrophoneAmplitudeProvider(mic);
        provider.Dispose();

        // After Dispose, subsequent raises should not update state
        var samples = new float[320];
        Array.Fill(samples, 0.9f);
        mic.Raise(samples, 16000);

        Assert.Equal(0f, provider.CurrentAmplitude);
    }

    /// <summary>
    ///     Minimal fake that exposes SamplesAvailable and a Raise(...) method. Other
    ///     IMicrophone / IAwaitableAudioSource / IAudioSource members throw to surface
    ///     unintended use during tests.
    /// </summary>
    private sealed class FakeMicrophone : IMicrophone
    {
        public event AudioSamplesHandler? SamplesAvailable;

        public void Raise(ReadOnlySpan<float> samples, int sampleRate) =>
            SamplesAvailable?.Invoke(samples, sampleRate);

        public void StartRecording() => throw new NotSupportedException();

        public void StopRecording() => throw new NotSupportedException();

        public IEnumerable<string> GetAvailableDevices() => throw new NotSupportedException();

        // IAwaitableAudioSource
        public bool IsFlushed => throw new NotSupportedException();

        public Task WaitForInitializationAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task WaitForNewSamplesAsync(long sampleCount, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task WaitForNewSamplesAsync(
            TimeSpan minimumDuration,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public void Flush() => throw new NotSupportedException();

        // IAudioSource
        public IReadOnlyDictionary<string, string> Metadata => throw new NotSupportedException();
        public TimeSpan Duration => throw new NotSupportedException();
        public TimeSpan TotalDuration => throw new NotSupportedException();
        public uint SampleRate => throw new NotSupportedException();
        public long FramesCount => throw new NotSupportedException();
        public ushort ChannelCount => throw new NotSupportedException();
        public bool IsInitialized => throw new NotSupportedException();
        public ushort BitsPerSample => throw new NotSupportedException();

        public Task<Memory<float>> GetSamplesAsync(
            long startFrame,
            int maxFrames = int.MaxValue,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Memory<byte>> GetFramesAsync(
            long startFrame,
            int maxFrames = int.MaxValue,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<int> CopyFramesAsync(
            Memory<byte> destination,
            long startFrame,
            int maxFrames = int.MaxValue,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public void Dispose() { }
    }
}
