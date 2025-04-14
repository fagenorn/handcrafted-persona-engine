using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using PersonaEngine.Lib.Audio.Player;
using PersonaEngine.Lib.Core.Conversation.Common.Messaging;
using PersonaEngine.Lib.Core.Conversation.Contracts.Events;
using PersonaEngine.Lib.Core.Conversation.Contracts.Interfaces;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Core.Conversation;

public class AudioOutputService(
    ITtsEngine                      ttsSynthesizer,
    IAggregatedStreamingAudioPlayer audioPlayer,
    IChannelRegistry                channelRegistry,
    ILogger<AudioOutputService>     logger)
    : IAudioOutputService
{
    private readonly ChannelWriter<object> _systemStateWriter = channelRegistry.SystemStateEvents.Writer;

    public async Task PlayTextStreamAsync(
        IAsyncEnumerable<string> textStream,
        DateTimeOffset?          utteranceStartTime,
        CancellationToken        cancellationToken)
    {
        logger.LogDebug("Starting audio playback process...");
        var stopReason               = AssistantStopReason.Unknown;

        try
        {
            logger.LogDebug("Requesting TTS synthesis stream...");
            var audioSegments = ttsSynthesizer.SynthesizeStreamingAsync(textStream, cancellationToken: cancellationToken);

            var eventPublishingAudioStream = WrapAudioStreamForEvents(
                                                                      audioSegments,
                                                                      utteranceStartTime,
                                                                      () => _ = true,
                                                                      cancellationToken);

            logger.LogDebug("Starting audio player...");
            await audioPlayer.StartPlaybackAsync(eventPublishingAudioStream, cancellationToken);

            if ( !cancellationToken.IsCancellationRequested )
            {
                stopReason = AssistantStopReason.CompletedNaturally;
                logger.LogInformation("Audio playback completed naturally.");
            }
            else
            {
                stopReason = AssistantStopReason.Cancelled;
                logger.LogInformation("Audio playback cancelled during playback.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopReason = AssistantStopReason.Cancelled;
            logger.LogInformation("Audio playback process cancelled.");
        }
        catch (Exception ex)
        {
            stopReason = AssistantStopReason.Error;
            logger.LogError(ex, "Error during audio playback process.");
        }
        finally
        {
            // Ensure AssistantSpeakingStopped is always published if started
            // or even if it failed before starting (reason: Error/Cancelled)
            logger.LogDebug("Publishing AssistantSpeakingStopped event. Reason: {Reason}", stopReason);
            _systemStateWriter.TryWrite(new AssistantSpeakingStopped(DateTimeOffset.Now, stopReason));
        }
    }

    public async Task StopPlaybackAsync()
    {
        logger.LogDebug("Stopping audio playback via AudioOutputService.");
        try
        {
            await audioPlayer.StopPlaybackAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error stopping audio player.");
            // Decide if this should re-throw or just be logged
        }
    }

    public ValueTask DisposeAsync()
    {
        logger.LogInformation("Disposing AudioOutputService.");

        return ValueTask.CompletedTask;
    }

    private async IAsyncEnumerable<AudioSegment> WrapAudioStreamForEvents(
        IAsyncEnumerable<AudioSegment>             source,
        DateTimeOffset?                            utteranceStartTime,
        Action                                     markSpeakingStartedPublished,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var        firstSegment = true;
        Stopwatch? firstAudioSw = null;

        await foreach ( var segment in source.WithCancellation(cancellationToken) )
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ( firstSegment )
            {
                firstSegment = false;
                firstAudioSw?.Stop();
                logger.LogDebug("First audio segment received from TTS. Latency: {Latency}ms", firstAudioSw?.ElapsedMilliseconds ?? -1);

                // Publish AssistantSpeakingStarted ONCE first audio data arrives
                logger.LogDebug("Publishing AssistantSpeakingStarted event.");
                // Use WriteAsync here? Could block if channel full. TryWrite might drop.
                // Let's use WriteAsync with a timeout/cancellation.

                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(1));
                    await _systemStateWriter.WriteAsync(new AssistantSpeakingStarted(DateTimeOffset.Now), cts.Token);
                    markSpeakingStartedPublished();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to write AssistantSpeakingStarted event to channel.");
                    // Continue playback anyway? Yes.
                }

                if ( utteranceStartTime.HasValue )
                {
                    var latency = DateTimeOffset.Now - utteranceStartTime.Value;
                    logger.LogInformation("End-to-end latency (user speech end to first audio): {Latency}ms", latency.TotalMilliseconds);
                }
            }

            yield return segment;
        }
    }
}