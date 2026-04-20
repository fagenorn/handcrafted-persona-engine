using System.Runtime.CompilerServices;
using PersonaEngine.Lib.Audio;
using PersonaEngine.Lib.Utils.Audio;
using PersonaEngine.Lib.Utils.Pooling;

namespace PersonaEngine.Lib.ASR.VAD;

internal class SileroVadDetector : IVadDetector
{
    private readonly int _minSilenceSamples;

    private readonly int _minSpeechSamples;

    private readonly SileroVadOnnxModel _model;

    private readonly float _negThreshold;

    private readonly float _threshold;

    public event Action<float>? ProbabilityObserved;

    public SileroVadDetector(
        VadDetectorOptions vadDetectorOptions,
        SileroVadOptions sileroVadOptions
    )
    {
        _model = new SileroVadOnnxModel(sileroVadOptions.ModelPath);
        _threshold = sileroVadOptions.Threshold;
        _negThreshold = _threshold - sileroVadOptions.ThresholdGap;
        _minSpeechSamples = (int)(16d * vadDetectorOptions.MinSpeechDuration.TotalMilliseconds);
        _minSilenceSamples = (int)(16d * vadDetectorOptions.MinSilenceDuration.TotalMilliseconds);
    }

    public async IAsyncEnumerable<VadSegment> DetectSegmentsAsync(
        IAudioSource source,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        ValidateSource(source);

        foreach (
            var segment in DetectSegments(
                await source.GetSamplesAsync(0, cancellationToken: cancellationToken)
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return segment;
        }
    }

    private IEnumerable<VadSegment> DetectSegments(Memory<float> samples)
    {
        var state = _model.CreateInferenceState();
        int? startingIndex = null;
        int? startingSilenceIndex = null;

        for (
            var i = 0;
            i < samples.Length - SileroConstants.BatchSize;
            i += SileroConstants.BatchSize
        )
        {
            Memory<float> slice;
            PooledArray<float>? rentedMemory = null;
            try
            {
                if (i == 0)
                {
                    var pooled = PooledArray<float>.Rent(
                        SileroConstants.ContextSize + SileroConstants.BatchSize
                    );
                    rentedMemory = pooled;
                    Array.Clear(pooled.Array, 0, SileroConstants.ContextSize);
                    samples
                        .Span[..SileroConstants.BatchSize]
                        .CopyTo(pooled.Array.AsSpan(SileroConstants.ContextSize));
                    slice = pooled.Array.AsMemory(
                        0,
                        SileroConstants.BatchSize + SileroConstants.ContextSize
                    );
                }
                else
                {
                    slice = samples.Slice(
                        i - SileroConstants.ContextSize,
                        SileroConstants.BatchSize + SileroConstants.ContextSize
                    );
                }

                var prob = _model.Call(slice, state);
                ProbabilityObserved?.Invoke(prob);
                if (!startingIndex.HasValue)
                {
                    if (prob > _threshold)
                    {
                        startingIndex = i;
                    }

                    continue;
                }

                // We are in speech
                if (prob > _threshold)
                {
                    startingSilenceIndex = null;

                    continue;
                }

                if (prob > _negThreshold)
                {
                    // We are still in speech and the current batch is between the threshold and the negative threshold
                    // We continue to the next batch
                    continue;
                }

                if (startingSilenceIndex == null)
                {
                    startingSilenceIndex = i;

                    continue;
                }

                var silenceLength = i - startingSilenceIndex.Value;

                if (silenceLength > _minSilenceSamples)
                {
                    // We have silence after speech exceeding the minimum silence duration
                    var length = i - startingIndex.Value;
                    if (length >= _minSpeechSamples)
                    {
                        yield return new VadSegment
                        {
                            StartTime = TimeSpan.FromMilliseconds(
                                startingIndex.Value * 1000d / SileroConstants.SampleRate
                            ),
                            Duration = TimeSpan.FromMilliseconds(
                                length * 1000d / SileroConstants.SampleRate
                            ),
                        };
                    }

                    startingIndex = null;
                    startingSilenceIndex = null;
                }
            }
            finally
            {
                rentedMemory?.Dispose();
            }
        }

        if (startingIndex.HasValue)
        {
            var length = samples.Length - startingIndex.Value;
            if (length >= _minSpeechSamples)
            {
                yield return new VadSegment
                {
                    StartTime = TimeSpan.FromMilliseconds(
                        startingIndex.Value * 1000d / SileroConstants.SampleRate
                    ),
                    Duration = TimeSpan.FromMilliseconds(
                        length * 1000d / SileroConstants.SampleRate
                    ),
                    IsIncomplete = true,
                };
            }
        }
    }

    private static void ValidateSource(IAudioSource source)
    {
        AudioValidation.RequireMono16kHz(source);
    }
}
