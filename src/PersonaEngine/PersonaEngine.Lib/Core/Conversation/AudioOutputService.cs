using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Audio.Player;
using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;
using PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Core.Conversation;

public class AudioOutputService(
    ITtsEngine ttsSynthesizer,
    IAggregatedStreamingAudioPlayer audioPlayer,
    IChannelRegistry channelRegistry,
    ILogger<AudioOutputService> logger)
    : IAudioOutputService
{
    private readonly ChannelWriter<object> _systemStateWriter = channelRegistry.SystemStateEvents.Writer;
    private bool _speakingStartedPublished = false; // Track if start event was published for the current stream

    public async Task PlayTextStreamAsync(
        Guid requestId, // Added RequestId
        IAsyncEnumerable<string> textStream,
        DateTimeOffset? utteranceStartTime,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Starting audio playback process (RequestId: {RequestId})...", requestId);
        var stopReason = AssistantStopReason.Unknown;
        _speakingStartedPublished = false; // Reset for this new request

        try
        {
            logger.LogDebug("Requesting TTS synthesis stream (RequestId: {RequestId})...", requestId);
            // Assuming ITtsEngine can handle cancellation appropriately
            var audioSegments = ttsSynthesizer.SynthesizeStreamingAsync(textStream, cancellationToken: cancellationToken);

            var eventPublishingAudioStream = WrapAudioStreamForEvents(
                requestId, // Pass RequestId
                audioSegments,
                utteranceStartTime,
                () => _speakingStartedPublished = true, // Mark as published via callback
                cancellationToken);

            logger.LogDebug("Starting audio player (RequestId: {RequestId})...", requestId);
            // Pass the cancellation token to the player
            await audioPlayer.StartPlaybackAsync(eventPublishingAudioStream, cancellationToken);

            // If playback finishes without external cancellation, it completed naturally
            if (!cancellationToken.IsCancellationRequested)
            {
                stopReason = AssistantStopReason.CompletedNaturally;
                logger.LogInformation("Audio playback completed naturally (RequestId: {RequestId}).", requestId);
            }
            else
            {
                // If cancellation was requested during playback
                stopReason = AssistantStopReason.Cancelled;
                logger.LogInformation("Audio playback cancelled during playback (RequestId: {RequestId}).", requestId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Catch cancellation that might occur before or during playback initiation
            stopReason = AssistantStopReason.Cancelled;
            logger.LogInformation("Audio playback process cancelled (RequestId: {RequestId}).", requestId);
        }
        catch (Exception ex)
        {
            stopReason = AssistantStopReason.Error;
            logger.LogError(ex, "Error during audio playback process (RequestId: {RequestId}).", requestId);
        }
        finally
        {
            // Ensure AssistantSpeakingStopped is always published IF speaking actually started.
            // Or even if it failed before starting (reason: Error/Cancelled)
            // We only publish Stopped if Started was published OR if it stopped due to cancellation/error *before* starting.
            if (_speakingStartedPublished || stopReason == AssistantStopReason.Cancelled || stopReason == AssistantStopReason.Error)
            {
                 logger.LogDebug("Publishing AssistantSpeakingStopped event (RequestId: {RequestId}). Reason: {Reason}", requestId, stopReason);
                 // Use TryWrite, WriteAsync could block if channel is full.
                 if(!_systemStateWriter.TryWrite(new AssistantSpeakingStopped(DateTimeOffset.Now, stopReason, requestId))) // Include RequestId
                 {
                      logger.LogWarning("Failed to write AssistantSpeakingStopped event to channel (RequestId: {RequestId}). Channel might be full or completed.", requestId);
                 }
            }
            else
            {
                 logger.LogDebug("Skipping AssistantSpeakingStopped event publication as AssistantSpeakingStarted was never published for this request (RequestId: {RequestId}).", requestId);
            }
        }
    }

    /// <summary>
    /// Stops playback via the underlying player. The player itself should handle
    /// cleanup and potentially trigger cancellation internally which leads to
    /// AssistantSpeakingStopped(Cancelled) being published by PlayTextStreamAsync's finally block.
    /// </summary>
    public async Task StopPlaybackAsync()
    {
        logger.LogDebug("Stopping audio playback via AudioOutputService -> audioPlayer.StopPlaybackAsync().");
        try
        {
            // This call should cause the awaited StartPlaybackAsync in PlayTextStreamAsync
            // to eventually throw OperationCanceledException or complete, triggering the finally block there.
            await audioPlayer.StopPlaybackAsync();
            logger.LogInformation("Audio player stop requested successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error stopping audio player via StopPlaybackAsync.");
            // Decide if this should re-throw or just be logged
            // Re-throwing might be appropriate if the caller needs to know stopping failed.
            // throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        logger.LogInformation("Disposing AudioOutputService.");
        // Dispose managed resources if any (e.g., if audioPlayer needs disposal)
        // await (audioPlayer as IAsyncDisposable)?.DisposeAsync();
        return ValueTask.CompletedTask;
    }

    private async IAsyncEnumerable<AudioSegment> WrapAudioStreamForEvents(
        Guid requestId, // Added RequestId
        IAsyncEnumerable<AudioSegment> source,
        DateTimeOffset? utteranceStartTime,
        Action markSpeakingStartedPublished, // Callback to mark event published
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var firstSegment = true;
        Stopwatch? firstAudioSw = null; // Consider initializing: Stopwatch.StartNew();

        await foreach (var segment in source.WithCancellation(cancellationToken))
        {
            // Check cancellation before processing/yielding
            cancellationToken.ThrowIfCancellationRequested();

            if (firstSegment)
            {
                firstSegment = false;
                firstAudioSw?.Stop(); // Stop stopwatch if initialized
                logger.LogDebug("First audio segment received from TTS (RequestId: {RequestId}). Latency: {Latency}ms", requestId, firstAudioSw?.ElapsedMilliseconds ?? -1);

                // Publish AssistantSpeakingStarted ONCE first audio data arrives
                logger.LogDebug("Publishing AssistantSpeakingStarted event (RequestId: {RequestId}).", requestId);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                try
                {
                    // Use WriteAsync with a timeout/cancellation to avoid blocking indefinitely
                    // Link to the overall cancellation token
                    cts.CancelAfter(TimeSpan.FromSeconds(2)); // Timeout for writing to channel

                    // Include RequestId
                    await _systemStateWriter.WriteAsync(new AssistantSpeakingStarted(DateTimeOffset.Now, requestId), cts.Token);
                    markSpeakingStartedPublished(); // Mark that the start event was successfully published
                }
                catch (OperationCanceledException writeEx) when (writeEx.CancellationToken == cts.Token && !cancellationToken.IsCancellationRequested)
                {
                     logger.LogWarning("Timeout writing AssistantSpeakingStarted event to channel (RequestId: {RequestId}).", requestId);
                     // Continue playback anyway? Yes. The finally block in PlayTextStreamAsync will still publish Stopped.
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                     logger.LogInformation("Writing AssistantSpeakingStarted cancelled by main token (RequestId: {RequestId}).", requestId);
                     throw; // Re-throw cancellation to stop enumeration
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to write AssistantSpeakingStarted event to channel (RequestId: {RequestId}).", requestId);
                    // Continue playback anyway? Yes.
                }

                if (utteranceStartTime.HasValue)
                {
                    var latency = DateTimeOffset.Now - utteranceStartTime.Value;
                    logger.LogInformation("End-to-end latency (user speech end to first audio - RequestId: {RequestId}): {Latency}ms", requestId, latency.TotalMilliseconds);
                }
            }

            yield return segment;
        }
        logger.LogDebug("Finished yielding audio segments for RequestId: {RequestId}", requestId);
    }
}
