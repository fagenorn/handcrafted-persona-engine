using System.Globalization;
using System.Runtime.CompilerServices;

using PersonaEngine.Lib.Audio;

using Whisper.net;

namespace PersonaEngine.Lib.ASR.Transcriber;

internal class WhisperNetSpeechTranscriptor(WhisperProcessor whisperProcessor) : ISpeechTranscriptor
{
    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(IAudioSource source, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if ( source.ChannelCount != 1 )
        {
            throw new NotSupportedException("Only mono-channel audio is supported. Consider one channel aggregation on the audio source.");
        }

        if ( source.SampleRate != 16000 )
        {
            throw new NotSupportedException("Only 16 kHz audio is supported. Consider resampling before calling this transcriptor.");
        }

        var samples = await source.GetSamplesAsync(0, cancellationToken: cancellationToken);

        await foreach ( var segment in whisperProcessor.ProcessAsync(samples, cancellationToken) )
        {
            yield return new TranscriptSegment {
                                                   Metadata        = source.Metadata,
                                                   StartTime       = segment.Start,
                                                   Duration        = segment.End - segment.Start,
                                                   ConfidenceLevel = segment.Probability,
                                                   Language        = new CultureInfo(segment.Language),
                                                   Text            = segment.Text,
                                                   Tokens = segment?.Tokens.Select(t => new TranscriptToken {
                                                                                                                Id                     = t.Id,
                                                                                                                Text                   = t.Text,
                                                                                                                Confidence             = t.Probability,
                                                                                                                ConfidenceLog          = t.ProbabilityLog,
                                                                                                                StartTime              = TimeSpan.FromMilliseconds(t.Start),
                                                                                                                Duration               = TimeSpan.FromMilliseconds(t.End - t.Start),
                                                                                                                DtwTimestamp           = t.DtwTimestamp,
                                                                                                                TimestampConfidence    = t.TimestampProbability,
                                                                                                                TimestampConfidenceSum = t.TimestampProbabilitySum
                                                                                                            }).ToList()
                                               };
        }
    }

    public async ValueTask DisposeAsync() { await whisperProcessor.DisposeAsync(); }
}