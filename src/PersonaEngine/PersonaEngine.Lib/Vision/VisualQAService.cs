using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.LLM;

namespace PersonaEngine.Lib.Vision;

public class VisualQAService : IVisualQAService
{
    private readonly WindowCaptureService _captureService;

    private readonly IVisualChatEngine _chatEngine;

    private readonly VisionConfig _config;

    private readonly SemaphoreSlim _fileChangeSemaphore = new(1, 1);

    private readonly CancellationTokenSource _internalCts = new();

    private readonly ILogger<VisualQAService> _logger;

    private DateTimeOffset _lastProcessedTimestamp = DateTimeOffset.MinValue;

    public VisualQAService(
        IOptions<AvatarAppConfig> config,
        WindowCaptureService captureService,
        IVisualChatEngine chatEngine,
        ILogger<VisualQAService> logger
    )
    {
        _config = config.Value.Vision;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _captureService = captureService;
        _chatEngine = chatEngine;

        _captureService.OnCaptureFrame += OnCaptureFrame;
    }

    public string? ScreenCaption { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return Task.CompletedTask;
        }

        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _internalCts.Token);
        _logger.LogDebug("Starting Visual QA Service");
        _captureService.StartAsync(cancellationToken);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _logger.LogDebug("Stopping Visual QA Service");
        await _internalCts.CancelAsync();
        await _captureService.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _internalCts.Dispose();
        _fileChangeSemaphore.Dispose();
        _captureService.OnCaptureFrame -= OnCaptureFrame;
    }

    private async void OnCaptureFrame(object? sender, CaptureFrameEventArgs e)
    {
        await _fileChangeSemaphore.WaitAsync(_internalCts.Token);

        try
        {
            var currentTimestamp = DateTimeOffset.Now;
            if ((currentTimestamp - _lastProcessedTimestamp).TotalMilliseconds < 100)
            {
                return;
            }

            _lastProcessedTimestamp = currentTimestamp;

            var result = await ProcessVisualQAFrame(e.FrameData);
            ScreenCaption = result;
        }
        finally
        {
            _fileChangeSemaphore.Release();
        }
    }

    private async Task<string?> ProcessVisualQAFrame(ReadOnlyMemory<byte> imageData)
    {
        try
        {
            var question =
                "Describe the main content of this screenshot objectivly in great detail.";
            var chatMessage = new VisualChatMessage(question, imageData);
            var caption = new StringBuilder();
            var settings = new OpenAIPromptExecutionSettings
            {
                FrequencyPenalty = 1.1,
                Temperature = 0.1,
                PresencePenalty = 1.1,
                MaxTokens = 256,
            };
            await foreach (
                var response in _chatEngine.GetStreamingChatResponseAsync(
                    chatMessage,
                    settings,
                    _internalCts.Token
                )
            )
            {
                caption.Append(response);
            }

            return caption.ToString();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to caption screen frame");

            return null;
        }
    }
}
