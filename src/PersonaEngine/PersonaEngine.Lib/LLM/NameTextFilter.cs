using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.LLM;

public class NameTextFilter : ITextFilter
{
    public int Priority => 9999;

    public ValueTask<TextFilterResult> ProcessAsync(string text, CancellationToken cancellationToken = default)
    {
        if ( string.IsNullOrEmpty(text) )
        {
            return ValueTask.FromResult(new TextFilterResult { ProcessedText = text ?? string.Empty });
        }

        var span = text.AsSpan();

        if ( span.Length <= 2 || span[0] != '[' )
        {
            return ValueTask.FromResult(new TextFilterResult { ProcessedText = text });
        }

        var closingBracketIndex = span.IndexOf(']');

        if ( closingBracketIndex <= 1 )
        {
            return ValueTask.FromResult(new TextFilterResult { ProcessedText = text });
        }

        var remainingSpan = span[(closingBracketIndex + 1)..];

        remainingSpan = remainingSpan.TrimStart();

        var processedText = remainingSpan.ToString();

        return ValueTask.FromResult(new TextFilterResult { ProcessedText = processedText });
    }

    public ValueTask PostProcessAsync(TextFilterResult textFilterResult, AudioSegment segment, CancellationToken cancellationToken = default) { return ValueTask.CompletedTask; }
}