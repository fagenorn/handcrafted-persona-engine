using System.Text;

namespace PersonaEngine.Lib.Utils;

public static class TextExtensions
{
    public static string SafeNormalizeUnicode(this string input)
    {
        if ( string.IsNullOrEmpty(input) )
        {
            return input;
        }

        try
        {
            return input.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // Failed,try to remove invalid unicode chars
        }

        // Remove invalid characters
        var result     = new StringBuilder(input.Length);
        var currentPos = 0;

        while ( currentPos < input.Length )
        {
            var invalidPos = FindInvalidCharIndex(input[currentPos..]);
            if ( invalidPos == -1 )
            {
                // No more invalid characters found, add the rest
                result.Append(input.AsSpan(currentPos));

                break;
            }

            // Add the valid portion before the invalid character
            if ( invalidPos > 0 )
            {
                result.Append(input.AsSpan(currentPos, invalidPos));
            }

            // Skip the invalid character (or pair)
            if ( input[currentPos + invalidPos] >= 0xD800 && input[currentPos + invalidPos] <= 0xDBFF )
            {
                // Skip surrogate pair
                currentPos += invalidPos + 2;
            }
            else
            {
                // Skip single character
                currentPos += invalidPos + 1;
            }
        }

        // Now normalize the cleaned string
        return result.ToString().Normalize(NormalizationForm.FormKC);
    }

    /// <summary>
    ///     Searches invalid charachters (non-chars defined in Unicode standard and invalid surrogate pairs) in a string
    /// </summary>
    /// <param name="aString"> the string to search for invalid chars </param>
    /// <returns>the index of the first bad char or -1 if no bad char is found</returns>
    private static int FindInvalidCharIndex(string aString)
    {
        for ( var i = 0; i < aString.Length; i++ )
        {
            int ch = aString[i];
            if ( ch < 0xD800 ) // char is up to first high surrogate
            {
                continue;
            }

            if ( ch is >= 0xD800 and <= 0xDBFF )
            {
                // found high surrogate -> check surrogate pair
                i++;
                if ( i == aString.Length )
                {
                    // last char is high surrogate, so it is missing its pair
                    return i - 1;
                }

                int chlow = aString[i];
                if ( chlow is < 0xDC00 or > 0xDFFF )
                {
                    // did not found a low surrogate after the high surrogate
                    return i - 1;
                }

                // convert to UTF32 - like in Char.ConvertToUtf32(highSurrogate, lowSurrogate)
                ch = (ch - 0xD800) * 0x400 + (chlow - 0xDC00) + 0x10000;
                if ( ch > 0x10FFFF )
                {
                    // invalid Unicode code point - maximum excedeed
                    return i;
                }

                if ( (ch & 0xFFFE) == 0xFFFE )
                {
                    // other non-char found
                    return i;
                }

                // found a good surrogate pair
                continue;
            }

            if ( ch is >= 0xDC00 and <= 0xDFFF )
            {
                // unexpected low surrogate
                return i;
            }

            if ( ch is >= 0xFDD0 and <= 0xFDEF )
            {
                // non-chars are considered invalid by System.Text.Encoding.GetBytes() and String.Normalize()
                return i;
            }

            if ( (ch & 0xFFFE) == 0xFFFE )
            {
                // other non-char found
                return i;
            }
        }

        return -1;
    }

    public static int GetWordCount(this string text)
    {
        if ( string.IsNullOrWhiteSpace(text) )
        {
            return 0;
        }

        var wordCount = 0;
        var inWord    = false;

        foreach ( var c in text )
        {
            if ( char.IsWhiteSpace(c) )
            {
                inWord = false;
            }
            else
            {
                if ( inWord )
                {
                    continue;
                }

                wordCount++;
                inWord = true;
            }
        }

        return wordCount;
    }
}