using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;
using PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;
using PersonaEngine.Lib.UI.Common;

using Silk.NET.OpenGL;

namespace PersonaEngine.Lib.Core.Conversation.Adapters;

/// <summary>
///     An output adapter that handles rendering text content as speech audio
///     by utilizing the IAudioOutputService.
/// </summary>
public sealed class AudioOutputAdapter(
    IAudioOutputService         audioOutputService,
    IChannelRegistry            channelRegistry,
    ILogger<AudioOutputAdapter> logger)
    : IOutputAdapter, IStartupTask
{
    private CancellationTokenSource? _cts;

    private Task? _executionTask;

    public Task StopAsync() { return StopAsync(CancellationToken.None); }

    public async ValueTask DisposeAsync()
    {
        logger.LogInformation("Disposing AudioOutputAdapter...");
        await StopAsync(CancellationToken.None);
        logger.LogInformation("AudioOutputAdapter disposed.");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting AudioOutputAdapter...");
        _cts           = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionTask = ProcessOutputLoop(channelRegistry.OutputRequests.Reader, _cts.Token);
        logger.LogInformation("AudioOutputAdapter started.");

        return Task.CompletedTask;
    }

    public void Execute(GL gl) { StartAsync(CancellationToken.None); }

    private async Task ProcessOutputLoop(ChannelReader<ProcessOutputRequest> reader, CancellationToken cancellationToken)
    {
        logger.LogDebug("AudioOutputAdapter listening for output requests...");
        try
        {
            await foreach ( var request in reader.ReadAllAsync(cancellationToken) )
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ( request.OutputTypeHint.Equals("Audio", StringComparison.OrdinalIgnoreCase) ||
                     request.OutputTypeHint.Equals("Default", StringComparison.OrdinalIgnoreCase) )
                {
                    logger.LogInformation("AudioOutputAdapter received request: {ContentSummary}", request.Content.Length > 50 ? request.Content[..50] + "..." : request.Content);

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
                    async IAsyncEnumerable<string> ContentStream() { yield return request.Content; }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

                    try
                    {
                        // TODO: Extract utterance start time from request metadata if available/needed
                        DateTimeOffset? utteranceStartTime = null;
                        // Example: if (request.Metadata?.TryGetValue("UtteranceStartTime", out var timeObj) == true && timeObj is DateTimeOffset time) utteranceStartTime = time;

                        // Call the underlying service to handle TTS and playback
                        // Use a separate CTS linked to the loop token for the playback itself?
                        // Or just pass the loop token? Passing the loop token means stopping the adapter cancels playback.
                        using var linkedPlaybackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        await audioOutputService.PlayTextStreamAsync(ContentStream(), utteranceStartTime, linkedPlaybackCts.Token);
                        logger.LogDebug("AudioOutputAdapter finished processing request for: {ContentSummary}", request.Content.Length > 50 ? request.Content[..50] + "..." : request.Content);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        logger.LogInformation("AudioOutputAdapter cancelled playback for request due to adapter stopping.");

                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "AudioOutputAdapter failed to process audio for request: {ContentSummary}", request.Content.Length > 50 ? request.Content[..50] + "..." : request.Content);
                    }
                }
                else
                {
                    logger.LogTrace("AudioOutputAdapter skipping request with OutputTypeHint: {Hint}", request.OutputTypeHint);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("AudioOutputAdapter processing loop cancelled.");
        }
        catch (ChannelClosedException)
        {
            logger.LogInformation("AudioOutputAdapter processing loop ended because the channel was closed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AudioOutputAdapter encountered an error in the processing loop.");
        }
        finally
        {
            logger.LogInformation("AudioOutputAdapter processing loop stopped.");
        }
    }

    private async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping AudioOutputAdapter...");
        if ( _cts == null || _executionTask == null )
        {
            logger.LogWarning("AudioOutputAdapter cannot stop, already stopped or never started.");

            return;
        }

        if ( !_cts.IsCancellationRequested )
        {
            await _cts.CancelAsync();
        }

        try
        {
            await _executionTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            logger.LogInformation("AudioOutputAdapter processing loop task completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("AudioOutputAdapter stop was cancelled by external token.");
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Timeout waiting for AudioOutputAdapter processing loop to stop.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during AudioOutputAdapter stop.");
        }
        finally
        {
            _cts?.Dispose();
            _cts           = null;
            _executionTask = null;
            logger.LogInformation("AudioOutputAdapter stopped.");
        }
    }
}