namespace PersonaEngine.Lib.TTS.Synthesis;

public class TokenContext
{
    public bool? FutureVowel { get; set; }

    public bool FutureTo { get; set; }

    public static TokenContext UpdateContext(TokenContext ctx, string? phonemes, Token token)
    {
        var vowel = ctx.FutureVowel;

        if (!string.IsNullOrEmpty(phonemes))
        {
            foreach (var c in phonemes)
            {
                if (
                    PhonemizerConstants.Vowels.Contains(c)
                    || PhonemizerConstants.Consonants.Contains(c)
                    || PhonemizerConstants.NonQuotePuncts.Contains(c)
                )
                {
                    vowel = PhonemizerConstants.NonQuotePuncts.Contains(c)
                        ? null
                        : PhonemizerConstants.Vowels.Contains(c);

                    break;
                }
            }
        }

        return new TokenContext { FutureVowel = vowel, FutureTo = token.IsTo() };
    }
}
