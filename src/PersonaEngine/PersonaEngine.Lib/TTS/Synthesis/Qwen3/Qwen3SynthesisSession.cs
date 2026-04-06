using System.Runtime.CompilerServices;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.TTS.Synthesis.Engine;

namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

/// <summary>
///     Per-turn synthesis session for Qwen3-TTS. Owns a <see cref="Qwen3StreamingAudioDecoder" />
///     that persists across sentences within the turn, providing cross-sentence audio
///     continuity through the decoder's KV-cache and convolutional history.
/// </summary>
internal sealed class Qwen3SynthesisSession : ISynthesisSession
{
    private readonly Qwen3TtsGgufEngine _engine;
    private readonly Qwen3StreamingAudioDecoder _decoder;
    private readonly Qwen3TtsOptions _options;
    private bool _disposed;

    public Qwen3SynthesisSession(Qwen3TtsGgufEngine engine, Qwen3TtsOptions options)
    {
        _engine = engine;
        _options = options;
        _decoder = engine.CreateAudioDecoder();
    }

    public async IAsyncEnumerable<AudioSegment> SynthesizeAsync(
        string sentence,
        bool isLastSegment,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            yield break;
        }

        var genOptions = new Qwen3GenerationOptions
        {
            Temperature = _options.Temperature,
            TopK = _options.TopK,
            TopP = _options.TopP,
            RepetitionPenalty = _options.RepetitionPenalty,
            MaxNewTokens = _options.MaxNewTokens,
            CodePredictorGreedy = _options.CodePredictorGreedy,
            SilencePenaltyEnabled = _options.SilencePenaltyEnabled,
        };

        await foreach (
            var segment in _engine
                .GenerateStreamingWithTimings(
                    _decoder,
                    sentence,
                    isLastSegment,
                    _options.Speaker,
                    _options.Language,
                    _options.Instruct,
                    genOptions,
                    _options.EmitEveryFrames,
                    cancellationToken
                )
                .ConfigureAwait(false)
        )
        {
            yield return segment;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _decoder.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
