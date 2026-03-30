namespace PersonaEngine.Lib.TTS.Synthesis;

public record ContextualPhonemeEntry(IReadOnlyDictionary<string, string> Forms) : PhonemeEntry
{
    public string? GetForm(string? tag, TokenContext? ctx)
    {
        // First try exact tag match
        if (tag != null && Forms.TryGetValue(tag, out var exactMatch))
        {
            return exactMatch;
        }

        // Try context-specific form
        if (ctx?.FutureVowel == null && Forms.TryGetValue("None", out var noneForm))
        {
            return noneForm;
        }

        // Try parent tag
        var parentTag = LexiconUtils.GetParentTag(tag);
        if (parentTag != null && Forms.TryGetValue(parentTag, out var parentMatch))
        {
            return parentMatch;
        }

        // Finally, use default
        return Forms.TryGetValue("DEFAULT", out var defaultForm) ? defaultForm : null;
    }
}
