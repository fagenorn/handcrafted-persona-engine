using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using PortAudioSharp;
using Stream = PortAudioSharp.Stream;

namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Represents a source that captures audio from a microphone.
/// </summary>
public sealed class MicrophoneInputPortAudioSource : AwaitableWaveFileSource, IMicrophone
{
    private readonly int _bitsPerSample;

    private readonly int _channels;

    private readonly CancellationTokenSource _cts;

    private readonly int _deviceNumber;

    private readonly uint _framesPerBuffer;

    private readonly ILogger _logger;

    private readonly int _sampleRate;

    private Stream? _audioStream;

    private bool _isRecording;

    private Dictionary<string, string> _metadata;

    public event AudioSamplesHandler? SamplesAvailable;

    public MicrophoneInputPortAudioSource(
        ILogger<MicrophoneInputPortAudioSource> logger,
        int deviceNumber = -1,
        int sampleRate = 16000,
        int bitsPerSample = 16,
        int channels = 1,
        uint framesPerBuffer = 0,
        bool storeSamples = true,
        bool storeBytes = false,
        int initialSizeFloats = DefaultInitialSize,
        int initialSizeBytes = DefaultInitialSize,
        IChannelAggregationStrategy? aggregationStrategy = null
    )
        : this(
            new Dictionary<string, string>(),
            logger,
            deviceNumber,
            sampleRate,
            bitsPerSample,
            channels,
            framesPerBuffer,
            storeSamples,
            storeBytes,
            initialSizeFloats,
            initialSizeBytes,
            aggregationStrategy
        ) { }

    private MicrophoneInputPortAudioSource(
        Dictionary<string, string> metadata,
        ILogger<MicrophoneInputPortAudioSource> logger,
        int deviceNumber = -1,
        int sampleRate = 16000,
        int bitsPerSample = 16,
        int channels = 1,
        uint framesPerBuffer = 0,
        bool storeSamples = true,
        bool storeBytes = false,
        int initialSizeFloats = DefaultInitialSize,
        int initialSizeBytes = DefaultInitialSize,
        IChannelAggregationStrategy? aggregationStrategy = null
    )
        : base(
            metadata,
            storeSamples,
            storeBytes,
            initialSizeFloats,
            initialSizeBytes,
            aggregationStrategy
        )
    {
        PortAudio.Initialize();

        _logger = logger;
        _deviceNumber = deviceNumber == -1 ? PortAudio.DefaultInputDevice : deviceNumber;
        _sampleRate = sampleRate;
        _bitsPerSample = bitsPerSample;
        _channels = channels;
        _framesPerBuffer = framesPerBuffer;
        _cts = new CancellationTokenSource();
        _metadata = metadata;

        if (_deviceNumber == PortAudio.NoDevice)
        {
            var sb = new StringBuilder();

            for (var i = 0; i != PortAudio.DeviceCount; ++i)
            {
                var deviceInfo = PortAudio.GetDeviceInfo(i);

                sb.AppendLine($"[*] Device {i}");
                sb.AppendLine($"        Name: {deviceInfo.name}");
                sb.AppendLine($"        Max input channels: {deviceInfo.maxInputChannels}");
                sb.AppendLine($"        Default sample rate: {deviceInfo.defaultSampleRate}");
            }

            logger.LogWarning("Devices available: {Devices}", sb);

            throw new InvalidOperationException("No default input device available");
        }

        Initialize(
            new AudioSourceHeader
            {
                BitsPerSample = (ushort)bitsPerSample,
                Channels = (ushort)channels,
                SampleRate = (uint)sampleRate,
            }
        );

        try
        {
            InitializeAudioStream();
        }
        catch
        {
            PortAudio.Terminate();

            throw;
        }
    }

    public void StartRecording()
    {
        if (_isRecording)
        {
            return;
        }

        _isRecording = true;
        _audioStream?.Start();
    }

    public void StopRecording()
    {
        if (!_isRecording)
        {
            return;
        }

        _isRecording = false;
        _audioStream?.Stop();
        Flush();
    }

    public IEnumerable<string> GetAvailableDevices()
    {
        throw new NotImplementedException();
    }

    private void InitializeAudioStream()
    {
        var deviceInfo = PortAudio.GetDeviceInfo(_deviceNumber);
        _logger.LogInformation("Using input device: {DeviceName}", deviceInfo.name);

        var inputParams = new StreamParameters
        {
            device = _deviceNumber,
            channelCount = _channels,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = deviceInfo.defaultLowInputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero,
        };

        _audioStream = new Stream(
            inputParams,
            null,
            _sampleRate,
            _framesPerBuffer,
            StreamFlags.ClipOff,
            ProcessAudioCallback,
            IntPtr.Zero
        );
    }

    private StreamCallbackResult ProcessAudioCallback(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData
    )
    {
        if (_cts.Token.IsCancellationRequested)
        {
            return StreamCallbackResult.Complete;
        }

        try
        {
            var samples = new float[frameCount];
            Marshal.Copy(input, samples, 0, (int)frameCount);

            var byteArray = SampleSerializer.Serialize(samples, Header.BitsPerSample);

            WriteData(byteArray);

            return StreamCallbackResult.Continue;
        }
        catch
        {
            return StreamCallbackResult.Abort;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            StopRecording();
            _audioStream?.Dispose();
            _audioStream = null;
            PortAudio.Terminate();
        }

        base.Dispose(disposing);
    }
}
