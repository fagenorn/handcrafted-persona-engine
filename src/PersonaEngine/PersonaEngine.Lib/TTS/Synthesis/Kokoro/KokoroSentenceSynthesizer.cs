using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.IO;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Engine;

namespace PersonaEngine.Lib.TTS.Synthesis.Kokoro;

/// <summary>
///     Kokoro ONNX-based sentence synthesizer.
///     Pipeline: text → phonemization → phoneme splitting → ONNX synthesis → token timing.
/// </summary>
internal sealed class KokoroSentenceSynthesizer : ISentenceSynthesizer
{
    private readonly ILogger<KokoroSentenceSynthesizer> _logger;
    private readonly IOptionsMonitor<KokoroVoiceOptions> _options;
    private readonly IPhonemizer _phonemizer;
    private readonly KokoroAudioSynthesizer _synthesizer;
    private readonly SemaphoreSlim _throttle;

    private bool _disposed;

    public KokoroSentenceSynthesizer(
        IPhonemizer phonemizer,
        IModelProvider modelProvider,
        IKokoroVoiceProvider voiceProvider,
        IOptionsMonitor<KokoroVoiceOptions> options,
        ILoggerFactory loggerFactory
    )
    {
        _phonemizer = phonemizer ?? throw new ArgumentNullException(nameof(phonemizer));
        _options = options;
        _logger = loggerFactory.CreateLogger<KokoroSentenceSynthesizer>();
        _synthesizer = new KokoroAudioSynthesizer(
            modelProvider,
            voiceProvider,
            options,
            loggerFactory.CreateLogger<KokoroAudioSynthesizer>()
        );
        _throttle = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
    }

    public string EngineId => "kokoro";

    public TtsEngineCapabilities Capabilities =>
        TtsEngineCapabilities.PhonemeControl | TtsEngineCapabilities.SpeedControl;

    public ISynthesisSession CreateSession() => new KokoroSynthesisSession(this);

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await _synthesizer.DisposeAsync();
            _throttle.Dispose();
            _disposed = true;
        }
    }

    internal async IAsyncEnumerable<AudioSegment> SynthesizeCoreAsync(
        string sentence,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            yield break;
        }

        await _throttle.WaitAsync(cancellationToken);

        var currentOptions = _options.CurrentValue;

        try
        {
            var phonemeResult = await _phonemizer.ToPhonemesAsync(sentence, cancellationToken);

            foreach (var phonemeChunk in SplitPhonemes(phonemeResult.Phonemes, 510))
            {
                var result = await _synthesizer.SynthesizeAsync(
                    phonemeChunk,
                    currentOptions,
                    cancellationToken
                );

                ApplyTokenTimings(phonemeResult.Tokens, currentOptions, result.PhonemeTimings);

                var segment = new AudioSegment(
                    result.Samples,
                    currentOptions.SampleRate,
                    phonemeResult.Tokens
                );

                yield return segment;
            }
        }
        finally
        {
            _throttle.Release();
        }
    }

    private static IEnumerable<string> SplitPhonemes(string phonemes, int maxLength)
    {
        if (string.IsNullOrEmpty(phonemes))
        {
            yield return string.Empty;
            yield break;
        }

        if (phonemes.Length <= maxLength)
        {
            yield return phonemes;
            yield break;
        }

        var currentIndex = 0;
        while (currentIndex < phonemes.Length)
        {
            var remainingLength = phonemes.Length - currentIndex;
            var chunkSize = Math.Min(maxLength, remainingLength);

            if (chunkSize < remainingLength && chunkSize > 10)
            {
                for (var i = currentIndex + chunkSize - 1; i > currentIndex + 10; i--)
                {
                    if (char.IsWhiteSpace(phonemes[i]) || IsPunctuation(phonemes[i]))
                    {
                        chunkSize = i - currentIndex + 1;
                        break;
                    }
                }
            }

            yield return phonemes.Substring(currentIndex, chunkSize);
            currentIndex += chunkSize;
        }
    }

    private static bool IsPunctuation(char c) => ".,:;!?-—()[]{}\"'".Contains(c);

    /// <summary>
    ///     Applies phoneme duration timing information to tokens.
    /// </summary>
    private static void ApplyTokenTimings(
        IReadOnlyList<Token> tokens,
        KokoroVoiceOptions options,
        ReadOnlyMemory<long> phonemeTimings
    )
    {
        if (tokens.Count == 0 || phonemeTimings.Length < 3)
        {
            return;
        }

        var timingsSpan = phonemeTimings.Span;

        const int TIME_DIVISOR = 80;

        var leftTime = options.TrimSilence ? 0 : 2 * Math.Max(0, timingsSpan[0] - 3);
        var rightTime = leftTime;

        var timingIndex = 1;
        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token.Phonemes))
            {
                if (token.Whitespace == " " && timingIndex + 1 < timingsSpan.Length)
                {
                    timingIndex++;
                    leftTime = rightTime + timingsSpan[timingIndex];
                    rightTime = leftTime + timingsSpan[timingIndex];
                    timingIndex++;
                }

                continue;
            }

            var endIndex = timingIndex + (token.Phonemes?.Length ?? 0);
            if (endIndex >= phonemeTimings.Length)
            {
                continue;
            }

            var startTime = (double)leftTime / TIME_DIVISOR;

            var tokenDuration = 0L;
            for (var i = timingIndex; i < endIndex && i < timingsSpan.Length; i++)
            {
                tokenDuration += timingsSpan[i];
            }

            var spaceDuration =
                token.Whitespace == " " && endIndex < timingsSpan.Length
                    ? timingsSpan[endIndex]
                    : 0;

            leftTime = rightTime + 2 * tokenDuration + spaceDuration;
            var endTime = (double)leftTime / TIME_DIVISOR;
            rightTime = leftTime + spaceDuration;

            token.StartTs = startTime;
            token.EndTs = endTime;

            timingIndex = endIndex + (token.Whitespace == " " ? 1 : 0);
        }
    }

    /// <summary>
    ///     Stateless session for Kokoro — <paramref name="isLastSegment" /> is ignored
    ///     since Kokoro has no cross-sentence decoder state.
    /// </summary>
    private sealed class KokoroSynthesisSession(KokoroSentenceSynthesizer owner) : ISynthesisSession
    {
        public IAsyncEnumerable<AudioSegment> SynthesizeAsync(
            string sentence,
            bool isLastSegment,
            CancellationToken cancellationToken = default
        ) => owner.SynthesizeCoreAsync(sentence, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
