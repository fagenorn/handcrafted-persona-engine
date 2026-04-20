using System.Text;
using System.Text.RegularExpressions;

namespace PersonaEngine.Lib.TTS.Synthesis;

internal class TextPreprocessor
{
    private readonly Regex _linkRegex;

    private readonly Regex _subtokenRegex;

    public TextPreprocessor()
    {
        _linkRegex = new Regex(@"\[([^\]]+)\]\(([^\)]*)\)", RegexOptions.Compiled);
        _subtokenRegex = new Regex(
            @"^[''']+|\p{Lu}(?=\p{Lu}\p{Ll})|(?:^-)?(?:\d?[,.]?\d)+|[-_]+|[''']{2,}|\p{L}*?(?:[''']\p{L})*?\p{Ll}(?=\p{Lu})|\p{L}+(?:[''']\p{L})*|[^-_\p{L}'''\d]|[''']+$",
            RegexOptions.Compiled
        );
    }

    public PreprocessedText Preprocess(string text)
    {
        text = text.TrimStart();
        var result = new StringBuilder(text.Length);
        var tokens = new List<string>();
        var features = new Dictionary<int, object>();

        var lastEnd = 0;

        foreach (Match m in _linkRegex.Matches(text))
        {
            if (m.Index > lastEnd)
            {
                var segment = text.Substring(lastEnd, m.Index - lastEnd);
                result.Append(segment);
                tokens.AddRange(segment.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }

            var linkText = m.Groups[1].Value;
            var featureText = m.Groups[2].Value;

            object? featureValue = null;
            if (featureText.Length >= 1)
            {
                if (
                    (featureText[0] == '-' || featureText[0] == '+')
                    && int.TryParse(featureText, out var intVal)
                )
                {
                    featureValue = intVal;
                }
                else if (int.TryParse(featureText, out intVal))
                {
                    featureValue = intVal;
                }
                else if (featureText == "0.5" || featureText == "+0.5")
                {
                    featureValue = 0.5;
                }
                else if (featureText == "-0.5")
                {
                    featureValue = -0.5;
                }
                else if (featureText.Length > 1)
                {
                    var firstChar = featureText[0];
                    var lastChar = featureText[featureText.Length - 1];

                    if (firstChar == '/' && lastChar == '/')
                    {
                        featureValue = firstChar + featureText.Substring(1, featureText.Length - 2);
                    }
                    else if (firstChar == '#' && lastChar == '#')
                    {
                        featureValue = firstChar + featureText.Substring(1, featureText.Length - 2);
                    }
                }
            }

            if (featureValue != null)
            {
                features[tokens.Count] = featureValue;
            }

            result.Append(linkText);
            tokens.Add(linkText);
            lastEnd = m.Index + m.Length;
        }

        if (lastEnd < text.Length)
        {
            var segment = text.Substring(lastEnd);
            result.Append(segment);
            tokens.AddRange(segment.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        return new PreprocessedText(result.ToString(), tokens, features);
    }

    public List<string> Subtokenize(string word)
    {
        var matches = _subtokenRegex.Matches(word);
        if (matches.Count == 0)
        {
            return new List<string> { word };
        }

        var result = new List<string>(matches.Count);
        foreach (Match match in matches)
        {
            result.Add(match.Value);
        }

        return result;
    }
}
