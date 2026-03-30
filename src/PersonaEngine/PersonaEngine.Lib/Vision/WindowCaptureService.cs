using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using uniffi.rust_lib;

namespace PersonaEngine.Lib.Vision;

public class CaptureFrameEventArgs : EventArgs
{
    public CaptureFrameEventArgs(ReadOnlyMemory<byte> frameData)
    {
        FrameData = frameData;
    }

    public ReadOnlyMemory<byte> FrameData { get; }
}

public class WindowCaptureService : IDisposable
{
    private readonly VisionConfig _config;

    private readonly CancellationTokenSource _cts = new();

    private readonly Lock _lock = new();

    private readonly ILogger<WindowCaptureService> _logger;

    private Task? _captureTask;

    public WindowCaptureService(
        IOptions<AvatarAppConfig> config,
        ILogger<WindowCaptureService>? logger = null
    )
    {
        _config = config.Value.Vision ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? NullLogger<WindowCaptureService>.Instance;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    public event EventHandler<CaptureFrameEventArgs>? OnCaptureFrame;

    private void HandleCaptureFrame(ReadOnlyMemory<byte> frameData)
    {
        try
        {
            OnCaptureFrame?.Invoke(this, new CaptureFrameEventArgs(frameData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error raising PlaybackStarted event");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_captureTask != null)
            {
                return Task.CompletedTask;
            }

            _captureTask = Task.Run(() => CaptureLoop(_cts.Token), cancellationToken);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        lock (_lock)
        {
            if (_captureTask == null)
            {
                return;
            }

            _cts.Cancel();
        }

        try
        {
            await _captureTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested.
        }
        finally
        {
            lock (_lock)
            {
                _captureTask = null;
            }
        }
    }

    private async Task CaptureLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (OnCaptureFrame == null)
                {
                    return;
                }

                var result = RustLibMethods.CaptureWindowByTitle(_config.WindowTitle);
                if (result.image != null)
                {
                    var imageData = await ProcessImage(result, token);
                    HandleCaptureFrame(imageData);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during capture");
            }

            try
            {
                await Task.Delay(_config.CaptureInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<ReadOnlyMemory<byte>> ProcessImage(
        ImageData imageData,
        CancellationToken token
    )
    {
        var minPixels = _config.CaptureMinPixels;
        var maxPixels = _config.CaptureMaxPixels;

        var width = (int)imageData.width;
        var height = (int)imageData.height;
        using var image = Image.LoadPixelData<Rgba32>(imageData.image, width, height);
        var currentPixels = width * height;
        if (currentPixels < minPixels || currentPixels > maxPixels)
        {
            double scaleFactor;
            if (currentPixels < minPixels)
            {
                scaleFactor = Math.Sqrt((double)minPixels / currentPixels);
            }
            else
            {
                scaleFactor = Math.Sqrt((double)maxPixels / currentPixels);
            }

            var newWidth = (int)(width * scaleFactor);
            var newHeight = (int)(height * scaleFactor);

            image.Mutate(x => x.Resize(newWidth, newHeight));
        }

        using var memStream = new MemoryStream();
        await image.SaveAsync(memStream, PngFormat.Instance, token);

        return new ReadOnlyMemory<byte>(memStream.ToArray());
    }
}
