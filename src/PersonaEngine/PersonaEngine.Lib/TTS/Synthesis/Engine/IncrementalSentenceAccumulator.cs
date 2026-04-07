using System.Text;
using PersonaEngine.Lib.TTS.Synthesis.TextProcessing;

namespace PersonaEngine.Lib.TTS.Synthesis.Engine;

/// <summary>
///     Buffers incoming LLM text chunks and yields completed sentences incrementally.
///     Uses a cheap punctuation pre-check to skip normalization + segmentation for
///     chunks that cannot contain sentence boundaries (~90% of LLM tokens).
/// </summary>
internal sealed class IncrementalSentenceAccumulator
{
    private static readonly char[] SentenceEndingChars = ['.', '!', '?', ';', ':', '\u2014'];

    private readonly ITextNormalizer _normalizer;
    private readonly ISentenceSegmenter _segmenter;
    private readonly StringBuilder _buffer = new(4096);

    public IncrementalSentenceAccumulator(
        ITextNormalizer normalizer,
        ISentenceSegmenter segmenter
    )
    {
        _normalizer = normalizer;
        _segmenter = segmenter;
    }

    public void Append(string chunk)
    {
        _buffer.Append(chunk);
    }

    public IReadOnlyList<string> TakeCompletedSentences()
    {
        if (_buffer.Length == 0)
        {
            return [];
        }

        if (!ContainsSentenceEndingPunctuation())
        {
            return [];
        }

        var text = _buffer.ToString();
        var normalized = _normalizer.Normalize(text);

        if (string.IsNullOrEmpty(normalized))
        {
            return [];
        }

        var sentences = _segmenter.Segment(normalized);

        if (sentences.Count <= 1)
        {
            return [];
        }

        _buffer.Clear();
        _buffer.Append(sentences[^1]);

        var completed = new List<string>(sentences.Count - 1);
        for (var i = 0; i < sentences.Count - 1; i++)
        {
            completed.Add(sentences[i]);
        }

        return completed;
    }

    public string? Flush()
    {
        if (_buffer.Length == 0)
        {
            return null;
        }

        var text = _buffer.ToString();
        var normalized = _normalizer.Normalize(text);

        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        var sentences = _segmenter.Segment(normalized);
        var joined = string.Join(" ", sentences).Trim();

        _buffer.Clear();

        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    public void Reset()
    {
        _buffer.Clear();
    }

    private bool ContainsSentenceEndingPunctuation()
    {
        foreach (var chunk in _buffer.GetChunks())
        {
            if (chunk.Span.IndexOfAny(SentenceEndingChars) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
