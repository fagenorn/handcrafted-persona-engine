using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NAudio.Wave;

using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.Audio;

/// <summary>
///     Represents a source that captures audio from a microphone,
///     configured dynamically via IOptionsMonitor.
/// </summary>
public sealed class MicrophoneInputNAudioSource : AwaitableWaveFileSource, IMicrophone
{
    private readonly Lock _lock = new();

    private readonly ILogger _logger;

    private readonly IDisposable? _optionsChangeListener;

    private MicrophoneConfiguration _currentOptions;

    private bool _isDisposed = false;

    private WaveInEvent? _microphoneIn;

    private CancellationTokenSource? _recordingCts;

    private bool _wasRecordingBeforeReconfigure = false;

    public MicrophoneInputNAudioSource(
        ILogger<MicrophoneInputNAudioSource>     logger,
        IOptionsMonitor<MicrophoneConfiguration> optionsMonitor,
        IChannelAggregationStrategy?             aggregationStrategy = null)
        : base(new Dictionary<string, string>(),
               true,
               false,
               DefaultInitialSize,
               DefaultInitialSize,
               aggregationStrategy)
    {
        _logger         = logger ?? throw new ArgumentNullException(nameof(logger));
        _currentOptions = optionsMonitor.CurrentValue;

        _logger.LogInformation("Initializing MicrophoneInputNAudioSource with options: {@Options}", _currentOptions);

        InitializeMicrophone(_currentOptions);

        _optionsChangeListener = optionsMonitor.OnChange(HandleOptionsChange);
    }

    public void StartRecording()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, typeof(MicrophoneInputNAudioSource));

            if ( _recordingCts is { IsCancellationRequested: false } )
            {
                _logger.LogWarning("StartRecording called, but recording is already active.");

                return;
            }

            StartRecordingInternal();
        }
    }

    public void StopRecording()
    {
        lock (_lock)
        {
            if ( _isDisposed )
            {
                return;
            }

            if ( _recordingCts == null || _recordingCts.IsCancellationRequested )
            {
                return;
            }

            StopInternal();
        }
    }

    public IEnumerable<string> GetAvailableDevices() { return GetAvailableDevicesInternal(_logger); }

    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private static IEnumerable<string> GetAvailableDevicesInternal(ILogger? logger = null)
    {
        var deviceNames = new List<string>();
        try
        {
            var deviceCount = WaveInEvent.DeviceCount;
            logger?.LogDebug("Enumerating {DeviceCount} audio input devices.", deviceCount);
            for ( var n = 0; n < deviceCount; n++ )
            {
                try
                {
                    var caps = WaveInEvent.GetCapabilities(n);
                    deviceNames.Add(caps.ProductName ?? $"Unnamed Device {n}");
                    logger?.LogTrace("Found Device {DeviceNumber}: {ProductName}", n, caps.ProductName);
                }
                catch (Exception capEx)
                {
                    logger?.LogError(capEx, "Error getting capabilities for WaveInEvent device number {DeviceNumber}.", n);
                    deviceNames.Add($"⚠️ {n}");
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error enumerating WaveInEvent devices.");

            return [];
        }

        return deviceNames;
    }

    private void HandleOptionsChange(MicrophoneConfiguration newOptions)
    {
        lock (_lock)
        {
            if ( _isDisposed )
            {
                return;
            }

            if ( newOptions.DeviceName == _currentOptions.DeviceName )
            {
                return;
            }

            _logger.LogInformation("Audio input configuration changed. Reconfiguring microphone. New options: {@Options}", newOptions);
            _currentOptions = newOptions;

            _wasRecordingBeforeReconfigure = _recordingCts is { IsCancellationRequested: false };

            StopInternal();

            InitializeMicrophone(newOptions);

            if ( !_wasRecordingBeforeReconfigure )
            {
                return;
            }

            _logger.LogInformation("Restarting recording after reconfiguration.");
            StartRecordingInternal();
        }
    }

    private void InitializeMicrophone(MicrophoneConfiguration options)
    {
        lock (_lock)
        {
            if ( _isDisposed )
            {
                return;
            }

            DisposeMicrophoneInstance();

            var deviceNumber = FindDeviceNumberByName(options.DeviceName);
            if ( deviceNumber == -1 )
            {
                _logger.LogWarning("Could not find audio input device named '{DeviceName}'. Falling back to default device (0).", options.DeviceName);
                deviceNumber = 0;
            }

            try
            {
                _microphoneIn = new WaveInEvent { DeviceNumber = deviceNumber, WaveFormat = new WaveFormat(16000, 16, 1) };

                if (!IsInitialized)
                {
                    Initialize(new AudioSourceHeader { BitsPerSample = 16, Channels = 1, SampleRate = 16000 });
                }

                _microphoneIn.DataAvailable    += WaveIn_DataAvailable;
                _microphoneIn.RecordingStopped += MicrophoneIn_RecordingStopped;

                _logger.LogInformation("Microphone initialized with DeviceNumber: {DeviceNumber}, WaveFormat: {WaveFormat}",
                                       _microphoneIn.DeviceNumber, _microphoneIn.WaveFormat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize WaveInEvent with DeviceNumber {DeviceNumber} and options {@Options}", deviceNumber, options);
                _microphoneIn = null;
            }
        }
    }

    private int FindDeviceNumberByName(string? deviceName)
    {
        if ( string.IsNullOrWhiteSpace(deviceName) )
        {
            _logger.LogDebug("Device name is null or whitespace, returning default device number 0.");

            return 0;
        }

        try
        {
            var devices = GetAvailableDevicesInternal();
            var index   = 0;
            foreach ( var name in devices )
            {
                _logger.LogTrace("Checking device {DeviceNumber}: {ProductName}", index, name);
                if ( deviceName.Trim().Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase) )
                {
                    _logger.LogDebug("Found device '{DeviceName}' at DeviceNumber {DeviceNumber}", deviceName, index);

                    return index;
                }

                index++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for device name '{DeviceName}'.", deviceName);
        }

        _logger.LogWarning("Audio input device named '{DeviceName}' not found among available devices.", deviceName);

        return -1;
    }

    private void StartRecordingInternal()
    {
        if ( _microphoneIn == null )
        {
            _logger.LogError("Cannot start recording, microphone is not initialized (likely due to previous error).");

            return;
        }

        try
        {
            _recordingCts = new CancellationTokenSource();
            _microphoneIn.StartRecording();
            _logger.LogInformation("Microphone recording started.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start microphone recording.");
            _recordingCts?.Dispose();
            _recordingCts = null;

            throw;
        }
    }

    private void StopInternal()
    {
        if ( _microphoneIn == null || _recordingCts == null || _recordingCts.IsCancellationRequested )
        {
            return;
        }

        _logger.LogInformation("Stopping microphone recording...");
        try
        {
            _recordingCts.Cancel();
            _microphoneIn?.StopRecording();
            
            _logger.LogInformation("Microphone recording stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during microphone StopRecording.");
        }
        finally
        {
            _recordingCts?.Dispose();
            _recordingCts = null;
        }
    }

    private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
    {
        // Use Task.Run to avoid blocking the NAudio callback thread
        // Especially important if WriteData involves significant processing or locking
        Task.Run(() =>
                 {
                     if ( _recordingCts is not { IsCancellationRequested: false } )
                     {
                         return;
                     }

                     // Create a copy of the buffer data if WriteData needs the Memory<byte>
                     // beyond the scope of this event handler, as NAudio might reuse the buffer.
                     // If WriteData processes it immediately and synchronously, AsMemory might be okay.
                     // Using ToArray() creates a safe copy.
                     var bufferCopy = new byte[e.BytesRecorded];
                     Buffer.BlockCopy(e.Buffer, 0, bufferCopy, 0, e.BytesRecorded);
                     WriteData(bufferCopy.AsMemory());
                 });
    }

    private void MicrophoneIn_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        _logger.LogDebug("MicrophoneIn_RecordingStopped event received.");
        if ( e.Exception != null )
        {
            _logger.LogError(e.Exception, "An error occurred during microphone recording.");
            // Decide how to handle this - maybe set an error state?
            // For now, just log it. Reconfiguration might fix it later.
        }

        _logger.LogDebug("Microphone stream flushed after stopping.");
    }

    private void DisposeMicrophoneInstance()
    {
        if ( _microphoneIn == null )
        {
            return;
        }

        _logger.LogDebug("Disposing existing WaveInEvent instance.");
        _microphoneIn.DataAvailable    -= WaveIn_DataAvailable;
        _microphoneIn.RecordingStopped -= MicrophoneIn_RecordingStopped;

        if ( _recordingCts is { IsCancellationRequested: false } )
        {
            _logger.LogWarning("Disposing microphone instance while it was potentially still marked as recording. Stopping first.");
            StopInternal();
        }
        
        _microphoneIn.Dispose();
        _microphoneIn = null;
    }

    protected override void Dispose(bool disposing)
    {
        if ( _isDisposed )
        {
            return;
        }

        if ( disposing )
        {
            _logger.LogInformation("Disposing MicrophoneInputNAudioSource...");
            lock (_lock)
            {
                _isDisposed = true;
                _optionsChangeListener?.Dispose();

                StopInternal();
                DisposeMicrophoneInstance();

                Flush();

                _recordingCts?.Dispose();
                _recordingCts = null;
            }

            _logger.LogInformation("MicrophoneInputNAudioSource disposed.");
        }

        base.Dispose(disposing);
    }
}