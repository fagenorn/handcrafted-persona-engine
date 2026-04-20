using System.Collections.Frozen;

namespace PersonaEngine.Lib.TTS.Synthesis;

public static class PhonemizerConstants
{
    public static readonly HashSet<char> Diphthongs = new("AIOQWYʤʧ");

    public static readonly HashSet<char> Vowels = new("AIOQWYaiuæɑɒɔəɛɜɪʊʌᵻ");

    public static readonly HashSet<char> Consonants = new("bdfhjklmnpstvwzðŋɡɹɾʃʒʤʧθ");

    public static readonly char PrimaryStress = 'ˈ';

    public static readonly char SecondaryStress = 'ˌ';

    public static readonly char[] Stresses = ['ˌ', 'ˈ'];

    public static readonly HashSet<char> UsTaus = new("AIOWYiuæɑəɛɪɹʊʌ");

    public static readonly HashSet<char> UsVocab = new(
        "AIOWYbdfhijklmnpstuvwzæðŋɑɔəɛɜɡɪɹɾʃʊʌʒʤʧˈˌθᵊᵻ"
    );

    public static readonly HashSet<char> GbVocab = new(
        "AIQWYabdfhijklmnpstuvwzðŋɑɒɔəɛɜɡɪɹʃʊʌʒʤʧˈˌːθᵊ"
    );

    public static readonly IReadOnlyDictionary<string, (string Dollar, string Cent)> Currencies =
        new Dictionary<string, (string, string)>
        {
            ["$"] = ("dollar", "cent"),
            ["£"] = ("pound", "pence"),
            ["€"] = ("euro", "cent"),
        }.ToFrozenDictionary();

    public static readonly Dictionary<string, string> AddSymbols = new()
    {
        ["."] = "dot",
        ["/"] = "slash",
    };

    public static readonly Dictionary<string, string> Symbols = new()
    {
        ["%"] = "percent",
        ["&"] = "and",
        ["+"] = "plus",
        ["@"] = "at",
    };

    public static readonly HashSet<string> Ordinals = ["st", "nd", "rd", "th"];

    public static readonly HashSet<string> PunctTags =
    [
        ".",
        ",",
        "-LRB-",
        "-RRB-",
        "``",
        "\"\"",
        "''",
        ":",
        "$",
        "#",
        "NFP",
    ];

    public static readonly Dictionary<string, string> PunctTagPhonemes = new()
    {
        ["-LRB-"] = "(",
        ["-RRB-"] = ")",
        ["``"] = "\u2014", // em dash
        ["\"\""] = "\u201D", // right double quote
        ["''"] = "\u201D", // right double quote
    };

    public static readonly HashSet<char> SubtokenJunks = new("',-._''/");

    public static readonly HashSet<char> Puncts = new(";:,.!?—…\"\"\"");

    public static readonly HashSet<char> NonQuotePuncts = [];

    static PhonemizerConstants()
    {
        // Initialize NonQuotePuncts
        foreach (var c in Puncts)
        {
            if (c != '"' && c != '"' && c != '"')
            {
                NonQuotePuncts.Add(c);
            }
        }
    }
}
