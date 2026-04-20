using System.Globalization;

namespace PersonaEngine.Lib.TTS.Synthesis;

public static class Num2Words
{
    private static readonly string[] _units =
    {
        "zero",
        "one",
        "two",
        "three",
        "four",
        "five",
        "six",
        "seven",
        "eight",
        "nine",
        "ten",
        "eleven",
        "twelve",
        "thirteen",
        "fourteen",
        "fifteen",
        "sixteen",
        "seventeen",
        "eighteen",
        "nineteen",
    };

    private static readonly string[] _tens =
    {
        "",
        "",
        "twenty",
        "thirty",
        "forty",
        "fifty",
        "sixty",
        "seventy",
        "eighty",
        "ninety",
    };

    private static readonly string[] _ordinalUnits =
    {
        "zeroth",
        "first",
        "second",
        "third",
        "fourth",
        "fifth",
        "sixth",
        "seventh",
        "eighth",
        "ninth",
        "tenth",
        "eleventh",
        "twelfth",
        "thirteenth",
        "fourteenth",
        "fifteenth",
        "sixteenth",
        "seventeenth",
        "eighteenth",
        "nineteenth",
    };

    private static readonly string[] _ordinalTens =
    {
        "",
        "",
        "twentieth",
        "thirtieth",
        "fortieth",
        "fiftieth",
        "sixtieth",
        "seventieth",
        "eightieth",
        "ninetieth",
    };

    public static string Convert(int number, string to = "cardinal")
    {
        if (number == 0)
        {
            return _units[0];
        }

        if (number < 0)
        {
            return "negative " + Convert(-number, to);
        }

        return to.ToLower() switch
        {
            "ordinal" => ConvertToOrdinal(number),
            "year" => ConvertToYear(number),
            _ => ConvertToCardinal(number),
        };
    }

    public static string Convert(double number)
    {
        // Handle integer part
        var intPart = (int)Math.Floor(number);
        var result = ConvertToCardinal(intPart);

        // Handle decimal part
        var decimalPart = number - intPart;
        if (decimalPart > 0)
        {
            // Get decimal digits as string and remove trailing zeros
            var decimalString = decimalPart
                .ToString("F20", CultureInfo.InvariantCulture)
                .TrimStart('0', '.')
                .TrimEnd('0');

            if (!string.IsNullOrEmpty(decimalString))
            {
                result += " point";
                foreach (var digit in decimalString)
                {
                    result += " " + _units[int.Parse(digit.ToString())];
                }
            }
        }

        return result;
    }

    private static string ConvertToCardinal(int number)
    {
        if (number < 20)
        {
            return _units[number];
        }

        if (number < 100)
        {
            return _tens[number / 10] + (number % 10 > 0 ? "-" + _units[number % 10] : "");
        }

        if (number < 1000)
        {
            return _units[number / 100]
                + " hundred"
                + (number % 100 > 0 ? " and " + ConvertToCardinal(number % 100) : "");
        }

        if (number < 1000000)
        {
            var thousands = number / 1000;
            var remainder = number % 1000;

            var result = ConvertToCardinal(thousands) + " thousand";

            if (remainder > 0)
            {
                // If remainder is less than 100, or if it has a tens/units part
                if (remainder < 100)
                {
                    result += " and " + ConvertToCardinal(remainder);
                }
                else
                {
                    // For numbers like 1,234 -> "one thousand two hundred and thirty-four"
                    var hundreds = remainder / 100;
                    var tensAndUnits = remainder % 100;

                    result += " " + _units[hundreds] + " hundred";
                    if (tensAndUnits > 0)
                    {
                        result += " and " + ConvertToCardinal(tensAndUnits);
                    }
                }
            }

            return result;
        }

        if (number < 1000000000)
        {
            return ConvertToCardinal(number / 1000000)
                + " million"
                + (
                    number % 1000000 > 0
                        ? (number % 1000000 < 100 ? " and " : " ")
                            + ConvertToCardinal(number % 1000000)
                        : ""
                );
        }

        return ConvertToCardinal(number / 1000000000)
            + " billion"
            + (
                number % 1000000000 > 0
                    ? (number % 1000000000 < 100 ? " and " : " ")
                        + ConvertToCardinal(number % 1000000000)
                    : ""
            );
    }

    private static string ConvertToOrdinal(int number)
    {
        if (number < 20)
        {
            return _ordinalUnits[number];
        }

        if (number < 100)
        {
            if (number % 10 == 0)
            {
                return _ordinalTens[number / 10];
            }

            return _tens[number / 10] + "-" + _ordinalUnits[number % 10];
        }

        var cardinal = ConvertToCardinal(number);

        // Replace the last word with its ordinal form
        var lastSpace = cardinal.LastIndexOf(' ');
        if (lastSpace == -1)
        {
            // Single word
            var lastWord = cardinal;
            if (lastWord.EndsWith("y"))
            {
                return lastWord.Substring(0, lastWord.Length - 1) + "ieth";
            }

            if (lastWord.EndsWith("eight"))
            {
                return lastWord + "h";
            }

            if (lastWord.EndsWith("nine"))
            {
                return lastWord + "th";
            }

            return lastWord + "th";
        }
        else
        {
            // Multiple words
            var lastWord = cardinal.Substring(lastSpace + 1);
            var prefix = cardinal.Substring(0, lastSpace + 1);

            if (lastWord.EndsWith("y"))
            {
                return prefix + lastWord.Substring(0, lastWord.Length - 1) + "ieth";
            }

            if (lastWord == "one")
            {
                return prefix + "first";
            }

            if (lastWord == "two")
            {
                return prefix + "second";
            }

            if (lastWord == "three")
            {
                return prefix + "third";
            }

            if (lastWord == "five")
            {
                return prefix + "fifth";
            }

            if (lastWord == "eight")
            {
                return prefix + "eighth";
            }

            if (lastWord == "nine" || lastWord == "twelve")
            {
                return prefix + lastWord + "th";
            }

            return prefix + lastWord + "th";
        }
    }

    private static string ConvertToYear(int year)
    {
        // Handle years specially
        if (year >= 2000)
        {
            // 2xxx is "two thousand [and] xxx"
            var remainder = year - 2000;
            if (remainder == 0)
            {
                return "two thousand";
            }

            if (remainder < 100)
            {
                return "two thousand and " + ConvertToCardinal(remainder);
            }

            return "two thousand " + ConvertToCardinal(remainder);
        }

        if (year >= 1000)
        {
            // Years like 1984 are "nineteen eighty-four"
            var century = year / 100;
            var remainder = year % 100;

            if (remainder == 0)
            {
                return ConvertToCardinal(century) + " hundred";
            }

            return ConvertToCardinal(century) + " " + ConvertToCardinal(remainder);
        }

        // Years less than 1000
        return ConvertToCardinal(year);
    }
}
