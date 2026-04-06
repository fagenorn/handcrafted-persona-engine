using PersonaEngine.Lib.TTS.Synthesis.Kokoro;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.Synthesis;

public class KokoroTokenConverterTests
{
    private static readonly Dictionary<char, long> PhonemeMap = new()
    {
        ['h'] = 10,
        ['e'] = 20,
        ['l'] = 30,
        ['o'] = 40,
    };

    [Fact]
    public void Convert_BasicPhonemes_WritesBosPhonemesEos()
    {
        var buffer = new long[10];

        var count = KokoroTokenConverter.Convert("helo", PhonemeMap, buffer);

        Assert.Equal(6, count); // BOS + 4 phonemes + EOS
        Assert.Equal(0L, buffer[0]); // BOS
        Assert.Equal(10L, buffer[1]); // h
        Assert.Equal(20L, buffer[2]); // e
        Assert.Equal(30L, buffer[3]); // l
        Assert.Equal(40L, buffer[4]); // o
        Assert.Equal(0L, buffer[5]); // EOS
    }

    [Fact]
    public void Convert_EmptyString_WritesOnlyBosEos()
    {
        var buffer = new long[10];

        var count = KokoroTokenConverter.Convert("", PhonemeMap, buffer);

        Assert.Equal(2, count); // BOS + EOS
        Assert.Equal(0L, buffer[0]); // BOS
        Assert.Equal(0L, buffer[1]); // EOS
    }

    [Fact]
    public void Convert_UnrecognizedPhoneme_CompactsOutput()
    {
        var buffer = new long[10];
        var count = KokoroTokenConverter.Convert("hxez", PhonemeMap, buffer);

        Assert.Equal(4, count); // BOS + h + e + EOS
        Assert.Equal(0L, buffer[0]); // BOS
        Assert.Equal(10L, buffer[1]); // h
        Assert.Equal(20L, buffer[2]); // e
        Assert.Equal(0L, buffer[3]); // EOS
    }

    [Fact]
    public void Convert_AllUnrecognized_WritesOnlyBosEos()
    {
        var buffer = new long[10];

        var count = KokoroTokenConverter.Convert("xyz", PhonemeMap, buffer);

        Assert.Equal(2, count); // BOS + EOS
        Assert.Equal(0L, buffer[0]); // BOS
        Assert.Equal(0L, buffer[1]); // EOS
    }
}
