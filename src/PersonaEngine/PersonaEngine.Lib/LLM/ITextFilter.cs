using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.LLM;

public interface ITextFilter
{
    int Priority { get; }

    ValueTask<TextFilterResult> ProcessAsync(string text, CancellationToken cancellationToken = default);

    ValueTask PostProcessAsync(TextFilterResult textFilterResult, AudioSegment segment, CancellationToken cancellationToken = default);
}

public record TextFilterResult
{
    public string ProcessedText { get; set; } = string.Empty;

    public Dictionary<string, object> Metadata { get; set; } = new();
}