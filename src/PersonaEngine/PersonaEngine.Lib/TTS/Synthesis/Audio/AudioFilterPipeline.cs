namespace PersonaEngine.Lib.TTS.Synthesis;

/// <summary>
///     Applies an ordered list of <see cref="IAudioFilter" /> to a stream of
///     <see cref="AudioSegment" /> chunks with transparent SOLA (Synchronized
///     Overlap-Add) stitching for <see cref="IBufferedAudioFilter" /> instances.
///     <para>
///         When no <see cref="IBufferedAudioFilter" /> is active, segments flow
///         through with zero overhead.
///     </para>
///     <para>Scoped to a single sentence — create one per sentence synthesis call.</para>
/// </summary>
internal sealed class AudioFilterPipeline
{
    private readonly IList<IAudioFilter> _orderedFilters;

    private readonly int _minSamples;

    // SOLA parameters (in input-rate samples, recomputed to output space at runtime)
    private readonly int _solaBufferSamples;
    private readonly int _solaSearchSamples;

    // Precomputed sin²/cos² fade windows (allocated once per pipeline, in output space)
    private float[]? _fadeInWindow;
    private float[]? _fadeOutWindow;
    private int _fadeWindowLen;

    // Accumulated input segments awaiting processing
    private readonly List<AudioSegment> _pending = [];
    private int _pendingSamples;

    // SOLA state carried between windows
    private float[]? _solaBuffer; // output tail from previous window for crossfade
    private bool _isFirstWindow = true;

    public AudioFilterPipeline(IList<IAudioFilter> orderedFilters)
    {
        _orderedFilters = orderedFilters;

        foreach (var filter in orderedFilters)
        {
            if (filter is IBufferedAudioFilter { MinimumSampleCount: > 0 } buffered)
            {
                _minSamples = Math.Max(_minSamples, buffered.MinimumSampleCount);
            }
        }

        // SOLA crossfade: 50 ms at 24 kHz = 1200 samples (will be scaled to actual rate)
        _solaBufferSamples = 1200;
        // SOLA search window: ~10 ms at 24 kHz = 240 samples (one pitch period)
        _solaSearchSamples = 240;
    }

    private bool RequiresBuffering => _minSamples > 0;

    public IEnumerable<AudioSegment> Submit(AudioSegment segment)
    {
        if (segment.AudioData.Length == 0)
        {
            yield return segment;
            yield break;
        }

        if (!RequiresBuffering)
        {
            ApplyAllFilters(segment);
            yield return segment;
            yield break;
        }

        _pending.Add(segment);
        _pendingSamples += segment.AudioData.Length;

        // Need extra samples beyond minimum for SOLA search + crossfade buffer
        var requiredSamples = _minSamples + _solaBufferSamples + _solaSearchSamples;
        if (_pendingSamples < requiredSamples)
        {
            yield break;
        }

        var result = ProcessWindow(isFinal: false);
        if (result is not null)
        {
            yield return result;
        }
    }

    public IEnumerable<AudioSegment> Flush()
    {
        if (!RequiresBuffering || _pendingSamples == 0)
        {
            yield break;
        }

        var result = ProcessWindow(isFinal: true);
        if (result is not null)
        {
            yield return result;
        }
    }

    private AudioSegment? ProcessWindow(bool isFinal)
    {
        var sentenceId = _pending[0].SentenceId;

        // 1. Build the input window from pending audio (no overlap prepending — reflection
        //    padding inside the filter itself now provides model context)
        var (windowAudio, windowSampleRate, windowTokens) = BuildWindow();

        // 2. Clear pending buffer
        _pending.Clear();
        _pendingSamples = 0;

        if (windowAudio.Length == 0)
        {
            return null;
        }

        // 3. Run all filters on the window
        var windowSegment = new AudioSegment(windowAudio.AsMemory(), windowSampleRate, windowTokens)
        {
            SentenceId = sentenceId,
        };
        ApplyAllFilters(windowSegment);

        var processedAudio = windowSegment.AudioData;

        if (processedAudio.Length == 0)
        {
            return null;
        }

        // Ensure fade windows are initialized for the actual output sample rate
        EnsureFadeWindows(windowSampleRate);

        var outputSpan = processedAudio.Span;
        var solaLen = _fadeWindowLen;
        var searchLen = Math.Min(
            _solaSearchSamples * windowSampleRate / 24000,
            Math.Max(0, processedAudio.Length - solaLen)
        );

        // 4. SOLA: find optimal alignment and crossfade with previous buffer
        int yieldStart = 0;

        if (_solaBuffer is not null && !_isFirstWindow)
        {
            if (processedAudio.Length >= solaLen + searchLen && solaLen > 0)
            {
                // Find best alignment via normalized cross-correlation
                var bestOffset = FindSolaOffset(outputSpan, _solaBuffer, solaLen, searchLen);

                // Apply sin²/cos² crossfade at the aligned position
                for (var i = 0; i < solaLen; i++)
                {
                    outputSpan[bestOffset + i] =
                        _solaBuffer[i] * _fadeOutWindow![i]
                        + outputSpan[bestOffset + i] * _fadeInWindow![i];
                }

                // Everything before bestOffset was already yielded via previous solaBuffer
                yieldStart = bestOffset;
            }
            else
            {
                // Output too short for SOLA — direct crossfade with available samples
                var fadeLen = Math.Min(
                    solaLen,
                    Math.Min(_solaBuffer.Length, processedAudio.Length)
                );
                for (var i = 0; i < fadeLen; i++)
                {
                    var t = (float)(i + 1) / (fadeLen + 1);
                    outputSpan[i] = _solaBuffer[i] * (1f - t) + outputSpan[i] * t;
                }
            }
        }

        // 5. Determine yield end and save tail for next SOLA crossfade
        int yieldEnd;

        if (isFinal)
        {
            yieldEnd = processedAudio.Length;
            _solaBuffer = null;
        }
        else
        {
            // Hold back solaLen samples at the end for next crossfade
            var holdBack = Math.Min(solaLen, processedAudio.Length - yieldStart);
            yieldEnd = processedAudio.Length - holdBack;

            if (yieldEnd <= yieldStart)
            {
                // Window too small to yield anything — save everything for next round
                _solaBuffer = outputSpan[yieldStart..].ToArray();
                _isFirstWindow = false;
                return null;
            }

            _solaBuffer = outputSpan[(processedAudio.Length - holdBack)..].ToArray();
        }

        _isFirstWindow = false;

        // 6. Slice the yield region and adjust token timestamps
        var yieldAudio = processedAudio[yieldStart..yieldEnd];
        var offsetSec = yieldStart / (double)windowSampleRate;

        IReadOnlyList<Token> yieldTokens;
        if (Math.Abs(offsetSec) < 0.0001 && yieldEnd == processedAudio.Length)
        {
            yieldTokens = windowTokens;
        }
        else
        {
            var adjusted = new List<Token>(windowTokens.Count);
            foreach (var token in windowTokens)
            {
                adjusted.Add(
                    token with
                    {
                        StartTs = token.StartTs.HasValue ? token.StartTs - offsetSec : null,
                        EndTs = token.EndTs.HasValue ? token.EndTs - offsetSec : null,
                    }
                );
            }
            yieldTokens = adjusted;
        }

        return new AudioSegment(yieldAudio, windowSampleRate, yieldTokens)
        {
            SentenceId = sentenceId,
        };
    }

    /// <summary>
    ///     Finds the offset within the SOLA search window that maximizes normalized
    ///     cross-correlation between the previous output tail and the current output.
    /// </summary>
    private static int FindSolaOffset(
        Span<float> currentOutput,
        float[] previousTail,
        int solaLen,
        int searchLen
    )
    {
        if (searchLen <= 0)
        {
            return 0;
        }

        var bestOffset = 0;
        var bestCorrelation = float.MinValue;

        // Precompute energy of previous tail (constant across all offsets)
        var prevEnergy = 0f;
        for (var i = 0; i < solaLen; i++)
        {
            prevEnergy += previousTail[i] * previousTail[i];
        }

        for (var offset = 0; offset <= searchLen; offset++)
        {
            float nom = 0;
            float curEnergy = 0;
            for (var i = 0; i < solaLen; i++)
            {
                var cur = currentOutput[offset + i];
                nom += previousTail[i] * cur;
                curEnergy += cur * cur;
            }

            // Normalized cross-correlation
            var den = MathF.Sqrt(prevEnergy * curEnergy) + 1e-8f;
            var correlation = nom / den;

            if (correlation > bestCorrelation)
            {
                bestCorrelation = correlation;
                bestOffset = offset;
            }
        }

        return bestOffset;
    }

    /// <summary>
    ///     Lazily initializes sin²/cos² fade windows scaled to the actual sample rate.
    /// </summary>
    private void EnsureFadeWindows(int sampleRate)
    {
        // 50 ms crossfade at the actual output sample rate
        var targetLen = sampleRate * 50 / 1000;

        if (_fadeInWindow is not null && _fadeWindowLen == targetLen)
        {
            return;
        }

        _fadeWindowLen = targetLen;
        _fadeInWindow = new float[targetLen];
        _fadeOutWindow = new float[targetLen];

        for (var i = 0; i < targetLen; i++)
        {
            var t = (float)i / (targetLen - 1);
            var sinVal = MathF.Sin(0.5f * MathF.PI * t);
            _fadeInWindow[i] = sinVal * sinVal;
            _fadeOutWindow[i] = 1f - _fadeInWindow[i];
        }
    }

    private (float[] Audio, int SampleRate, List<Token> Tokens) BuildWindow()
    {
        var totalLen = _pendingSamples;
        var sampleRate = _pending[0].SampleRate;

        var audio = new float[totalLen];
        var offset = 0;

        var tokens = new List<Token>();
        var audioOffsetSec = 0.0;

        foreach (var seg in _pending)
        {
            seg.AudioData.Span.CopyTo(audio.AsSpan(offset));

            foreach (var token in seg.Tokens)
            {
                tokens.Add(
                    token with
                    {
                        StartTs = token.StartTs.HasValue ? token.StartTs + audioOffsetSec : null,
                        EndTs = token.EndTs.HasValue ? token.EndTs + audioOffsetSec : null,
                    }
                );
            }

            audioOffsetSec += seg.AudioData.Length / (double)sampleRate;
            offset += seg.AudioData.Length;
        }

        return (audio, sampleRate, tokens);
    }

    private void ApplyAllFilters(AudioSegment segment)
    {
        foreach (var filter in _orderedFilters)
        {
            filter.Process(segment);
        }
    }
}
