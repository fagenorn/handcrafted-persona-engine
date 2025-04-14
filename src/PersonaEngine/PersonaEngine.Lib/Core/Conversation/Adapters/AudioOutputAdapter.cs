using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;
using PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;
using PersonaEngine.Lib.UI.Common;

using Silk.NET.OpenGL;
// Required for IAsyncEnumerable
// Assuming IStartupTask is here

// Assuming IStartupTask dependency

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
    private CancellationTokenSource? _adapterLoopCts; // CTS for the adapter's own processing loop

    private Task? _executionTask;

    // No longer needed here, managed within PlayTextStreamAsync
    // private CancellationTokenSource? _currentPlaybackCts = null;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting AudioOutputAdapter...");
        // Create a CTS linked to the application's shutdown token
        _adapterLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionTask  = ProcessOutputLoop(channelRegistry.OutputRequests.Reader, _adapterLoopCts.Token);
        logger.LogInformation("AudioOutputAdapter started.");

        return Task.CompletedTask;
    }

    // Explicit implementation for IOutputAdapter if needed, otherwise covered by StopAsync(CancellationToken)
    public Task StopAsync()
    {
        return StopAsync(CancellationToken.None); // Call the private method
    }

    public async ValueTask DisposeAsync()
    {
        logger.LogInformation("Disposing AudioOutputAdapter...");
        await StopAsync(CancellationToken.None); // Ensure stopped on dispose
        _adapterLoopCts?.Dispose();
        logger.LogInformation("AudioOutputAdapter disposed.");
    }

    // Implementation for IStartupTask
    public void Execute(GL gl)
    {
        // Consider if Execute should use a specific token or CancellationToken.None
        _ = StartAsync(CancellationToken.None);
    }

    private async Task ProcessOutputLoop(ChannelReader<ProcessOutputRequest> reader, CancellationToken cancellationToken)
    {
        logger.LogDebug("AudioOutputAdapter listening for output requests...");
        try
        {
            await foreach ( var request in reader.ReadAllAsync(cancellationToken) )
            {
                // Check for cancellation before processing each request
                cancellationToken.ThrowIfCancellationRequested();

                if ( request == null )
                {
                    logger.LogWarning("Read a null request from the output channel.");

                    continue;
                }

                if ( request.OutputTypeHint.Equals("Audio", StringComparison.OrdinalIgnoreCase) ||
                     request.OutputTypeHint.Equals("Default", StringComparison.OrdinalIgnoreCase) )
                {
                    logger.LogInformation("AudioOutputAdapter received request (RequestId: {RequestId}): {ContentSummary}",
                                          request.RequestId,
                                          request.Content.Length > 50 ? request.Content[..50] + "..." : request.Content);

                    // Local function to create the text stream
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
                    async IAsyncEnumerable<string> ContentStream() { yield return request.Content; }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

                    try
                    {
                        // Extract utterance start time from request metadata if available/needed
                        DateTimeOffset? utteranceStartTime = null;
                        if ( request.Metadata?.TryGetValue("UtteranceEndTime", out var timeObj) == true && timeObj is DateTimeOffset time )
                        {
                            utteranceStartTime = time;
                            logger.LogDebug("Extracted UtteranceEndTime {Time} for RequestId {RequestId}", utteranceStartTime, request.RequestId);
                        }

                        // *** IMPORTANT: Playback Cancellation ***
                        // The cancellation token passed here should ideally be controlled
                        // by the ORCHESTRATOR if it needs fine-grained control per-request.
                        // However, the current orchestrator cancels its *processing task* CTS,
                        // which doesn't directly map to stopping audio *after* the request is sent.
                        // Passing the adapter's loop token means stopping the adapter stops playback.
                        // A better approach (if orchestrator needs control) is for the orchestrator
                        // to maintain a CTS per request and have a mechanism to cancel it (e.g., via another channel).
                        // For now, we rely on the orchestrator calling audioOutputService.StopPlaybackAsync() for explicit stops.
                        // We pass the adapter's loop token for general shutdown cancellation.

                        // Pass the RequestId to the service
                        await audioOutputService.PlayTextStreamAsync(
                                                                     request.RequestId,
                                                                     ContentStream(),
                                                                     utteranceStartTime,
                                                                     cancellationToken); // Use the loop's cancellation token

                        logger.LogDebug("AudioOutputAdapter finished processing request (RequestId: {RequestId}).", request.RequestId);
                    }
                    // Catch cancellation specific to this playback attempt (if using a per-request token)
                    // catch (OperationCanceledException) when (specificRequestCts.IsCancellationRequested) { ... }
                    // Catch cancellation of the main adapter loop
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        logger.LogInformation("AudioOutputAdapter cancelled playback for request (RequestId: {RequestId}) due to adapter stopping.", request.RequestId);

                        throw; // Re-throw to stop the loop
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "AudioOutputAdapter failed to process audio for request (RequestId: {RequestId}): {ContentSummary}",
                                        request.RequestId,
                                        request.Content.Length > 50 ? request.Content[..50] + "..." : request.Content);
                        // Optionally publish an error event back?
                    }
                }
                else
                {
                    logger.LogTrace("AudioOutputAdapter skipping request (RequestId: {RequestId}) with OutputTypeHint: {Hint}", request.RequestId, request.OutputTypeHint);
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

    // Renamed from StopAsync() to avoid conflict if implementing multiple interfaces with StopAsync
    private async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping AudioOutputAdapter...");
        if ( _adapterLoopCts == null || _executionTask == null )
        {
            logger.LogWarning("AudioOutputAdapter cannot stop, already stopped or never started.");

            return;
        }

        if ( !_adapterLoopCts.IsCancellationRequested )
        {
            logger.LogDebug("Requesting cancellation of AudioOutputAdapter loop.");
            // Use CancelAsync for graceful cancellation if available, otherwise Cancel()
            try
            {
                await _adapterLoopCts.CancelAsync();
            }
            catch
            {
                _adapterLoopCts.Cancel();
            }
        }

        try
        {
            logger.LogDebug("Waiting for AudioOutputAdapter processing loop task to complete...");
            // Wait for the loop task to finish, respecting the external cancellation token if provided
            await Task.WhenAny(_executionTask, Task.Delay(Timeout.Infinite, cancellationToken));

            if ( _executionTask.IsCompleted )
            {
                logger.LogInformation("AudioOutputAdapter processing loop task completed (Status: {Status}).", _executionTask.Status);
            }
            else
            {
                logger.LogWarning("AudioOutputAdapter stop was cancelled by the provided cancellation token before the loop task finished.");
                // The loop task might still be running here if cancellationToken was triggered
            }

            // Await the task again to propagate any exceptions from the loop
            await _executionTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // This catches cancellation of the *waiting* task, not necessarily the loop task itself
            logger.LogWarning("Waiting for AudioOutputAdapter stop was cancelled by the provided cancellation token.");
        }
        catch (Exception ex) when (_executionTask.IsFaulted)
        {
            // Catch exceptions propagated from the execution task
            logger.LogError(ex, "Exception occurred in the AudioOutputAdapter processing loop during shutdown.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during AudioOutputAdapter stop waiting logic.");
        }
        finally
        {
            // Dispose the CTS *after* waiting for the task
            _adapterLoopCts?.Dispose(); // Dispose CTS even if waiting timed out or was cancelled
            _adapterLoopCts = null;
            _executionTask  = null; // Clear the task reference
            logger.LogInformation("AudioOutputAdapter stopped.");
        }
    }
}