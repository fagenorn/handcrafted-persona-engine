using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;

namespace PersonaEngine.Lib.TTS.Synthesis;

public static class LexiconUtils
{
    private static readonly ConcurrentDictionary<string, string> _parentTagCache = new(
        StringComparer.Ordinal
    );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string? GetParentTag(string? tag)
    {
        if (tag == null)
        {
            return null;
        }

        if (_parentTagCache.TryGetValue(tag, out var cachedParent))
        {
            return cachedParent;
        }

        string parent;
        if (tag.StartsWith("VB"))
        {
            parent = "VERB";
        }
        else if (tag.StartsWith("NN"))
        {
            parent = "NOUN";
        }
        else if (tag.StartsWith("ADV") || tag.StartsWith("RB"))
        {
            parent = "ADV";
        }
        else if (tag.StartsWith("ADJ") || tag.StartsWith("JJ"))
        {
            parent = "ADJ";
        }
        else
        {
            parent = tag;
        }

        _parentTagCache[tag] = parent;

        return parent;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Capitalize(ReadOnlySpan<char> str)
    {
        if (str.IsEmpty)
        {
            return string.Empty;
        }

        // Fast path for single-character strings
        if (str.Length == 1)
        {
            return char.ToUpper(str[0]).ToString();
        }

        // Avoid allocation if already capitalized
        if (char.IsUpper(str[0]))
        {
            return str.ToString();
        }

        var result = new StringBuilder(str.Length);
        result.Append(char.ToUpper(str[0]));
        result.Append(str.Slice(1));

        return result.ToString();
    }
}
