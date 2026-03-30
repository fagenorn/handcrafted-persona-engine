using Whisper.net;

namespace PersonaEngine.Lib.Configuration;

public record AsrConfiguration
{
    public WhisperConfigTemplate TtsMode { get; init; } = WhisperConfigTemplate.Performant;

    public string TtsPrompt { get; init; } = string.Empty;

    public float VadThreshold { get; init; } = 0.5f;

    public float VadThresholdGap { get; init; } = 0.15f;

    public float VadMinSpeechDuration { get; init; } = 250f;

    public float VadMinSilenceDuration { get; init; } = 200f;
}

public enum WhisperConfigTemplate
{
    Performant,

    Balanced,

    Precise,
}

public static class WhisperConfigTemplateExtensions
{
    public static WhisperProcessorBuilder ApplyTemplate(
        this WhisperProcessorBuilder builder,
        WhisperConfigTemplate template
    )
    {
        switch (template)
        {
            case WhisperConfigTemplate.Performant:
                var sampleBuilderA = (GreedySamplingStrategyBuilder)
                    builder.WithGreedySamplingStrategy();
                sampleBuilderA.WithBestOf(1);

                builder = sampleBuilderA.ParentBuilder;
                builder.WithStringPool();

                break;
            case WhisperConfigTemplate.Balanced:
                var sampleBuilderB = (BeamSearchSamplingStrategyBuilder)
                    builder.WithBeamSearchSamplingStrategy();
                sampleBuilderB.WithBeamSize(2);
                sampleBuilderB.WithPatience(1f);

                builder = sampleBuilderB.ParentBuilder;
                builder.WithStringPool();
                builder.WithTemperature(0.0f);

                break;
            case WhisperConfigTemplate.Precise:
                var sampleBuilderC = (BeamSearchSamplingStrategyBuilder)
                    builder.WithBeamSearchSamplingStrategy();
                sampleBuilderC.WithBeamSize(5);
                sampleBuilderC.WithPatience(1f);

                builder = sampleBuilderC.ParentBuilder;
                builder.WithStringPool();
                builder.WithTemperature(0.0f);

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(template), template, null);
        }

        return builder;
    }
}
