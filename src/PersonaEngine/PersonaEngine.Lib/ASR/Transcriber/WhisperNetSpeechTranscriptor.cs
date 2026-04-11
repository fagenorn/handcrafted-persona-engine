using System.Globalization;
using System.Runtime.CompilerServices;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Utils.Audio;
using Whisper.net;

namespace PersonaEngine.Lib.ASR.Transcriber;

internal class WhisperNetSpeechTranscriptor(WhisperProcessor whisperProcessor) : ISpeechTranscriptor
{
    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAudioSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        AudioValidation.RequireMono16kHz(source);

        var samples = await source.GetSamplesAsync(0, cancellationToken: cancellationToken);

        await foreach (var segment in whisperProcessor.ProcessAsync(samples, cancellationToken))
        {
            yield return new TranscriptSegment
            {
                Metadata = source.Metadata,
                StartTime = segment.Start,
                Duration = segment.End - segment.Start,
                ConfidenceLevel = segment.Probability,
                Language = new CultureInfo(segment.Language),
                Text = segment.Text,
                Tokens = segment
                    ?.Tokens.Select(t => new TranscriptToken
                    {
                        Id = t.Id,
                        Text = t.Text,
                        Confidence = t.Probability,
                        ConfidenceLog = t.ProbabilityLog,
                        StartTime = TimeSpan.FromMilliseconds(t.Start),
                        Duration = TimeSpan.FromMilliseconds(t.End - t.Start),
                        DtwTimestamp = t.DtwTimestamp,
                        TimestampConfidence = t.TimestampProbability,
                        TimestampConfidenceSum = t.TimestampProbabilitySum,
                    })
                    .ToList(),
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        await whisperProcessor.DisposeAsync();
    }
}
