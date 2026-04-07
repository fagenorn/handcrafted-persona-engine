using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Audio;
using PersonaEngine.Lib.TTS.Synthesis.Engine;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.Synthesis.Engine;

public class SentenceProcessorTests
{
    private readonly ISynthesisSession _session = Substitute.For<ISynthesisSession>();
    private readonly IPhonemizer _phonemizer = Substitute.For<IPhonemizer>();
    private readonly ILoggerFactory _loggerFactory = Substitute.For<ILoggerFactory>();

    public SentenceProcessorTests()
    {
        _loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());
    }

    private SentenceProcessor CreateProcessor(
        IEnumerable<ITextFilter>? textFilters = null,
        IEnumerable<IAudioFilter>? audioFilters = null
    ) => new(textFilters ?? [], audioFilters ?? [], _phonemizer, _loggerFactory);

    [Fact]
    public async Task ProcessAsync_NoFilters_YieldsSegmentsFromSession()
    {
        var processor = CreateProcessor();
        var tokens = new List<Token> { new() { Text = "Hello" } };
        var segment = new AudioSegment(new float[] { 1f, 2f, 3f }, 24000, tokens);

        _session
            .SynthesizeAsync("Hello", false, Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(segment));

        var results = new List<AudioSegment>();
        await foreach (
            var s in processor.ProcessAsync(
                _session,
                TtsEngineCapabilities.ProvidesPhonemes,
                "Hello",
                isLastSegment: false,
                CancellationToken.None
            )
        )
        {
            results.Add(s);
        }

        Assert.Single(results);
        Assert.Equal(24000, results[0].SampleRate);
    }

    [Fact]
    public async Task ProcessAsync_TextFilterApplied_SessionReceivesFilteredText()
    {
        var textFilter = Substitute.For<ITextFilter>();
        textFilter.Priority.Returns(1);
        textFilter
            .ProcessAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TextFilterResult { ProcessedText = "Filtered" });

        var processor = CreateProcessor(textFilters: [textFilter]);
        var segment = new AudioSegment(new float[] { 1f }, 24000, new List<Token>());

        _session
            .SynthesizeAsync("Filtered", false, Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(segment));

        var results = new List<AudioSegment>();
        await foreach (
            var s in processor.ProcessAsync(
                _session,
                TtsEngineCapabilities.ProvidesPhonemes,
                "Hello",
                isLastSegment: false,
                CancellationToken.None
            )
        )
        {
            results.Add(s);
        }

        Assert.Single(results);
        await textFilter.Received(1).ProcessAsync("Hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ProvidesPhonemes_DoesNotCallPhonemizer()
    {
        var processor = CreateProcessor();
        var segment = new AudioSegment(new float[] { 1f }, 24000, new List<Token>());

        _session
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(segment));

        await foreach (
            var _ in processor.ProcessAsync(
                _session,
                TtsEngineCapabilities.ProvidesPhonemes,
                "Hello",
                isLastSegment: false,
                CancellationToken.None
            )
        ) { }

        await _phonemizer
            .DidNotReceive()
            .ToPhonemesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_NoProvidesPhonemes_CallsPhonemizerAndEnrichesTokens()
    {
        var processor = CreateProcessor();

        var engineTokens = new List<Token> { new() { Text = "Hello" } };
        var segment = new AudioSegment(new float[] { 1f }, 24000, engineTokens);

        var phonemeTokens = new List<Token>
        {
            new()
            {
                Text = "Hello",
                Phonemes = "hɛloʊ",
                Tag = "UH",
                Stress = 1.0,
            },
        };

        _phonemizer
            .ToPhonemesAsync("Hello", Arg.Any<CancellationToken>())
            .Returns(new PhonemeResult("hɛloʊ", phonemeTokens));

        _session
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(segment));

        var results = new List<AudioSegment>();
        await foreach (
            var s in processor.ProcessAsync(
                _session,
                TtsEngineCapabilities.None,
                "Hello",
                isLastSegment: false,
                CancellationToken.None
            )
        )
        {
            results.Add(s);
        }

        Assert.Single(results);
        Assert.Equal("hɛloʊ", results[0].Tokens[0].Phonemes);
        Assert.Equal("UH", results[0].Tokens[0].Tag);
    }

    [Fact]
    public async Task ProcessAsync_EmotionMarkers_StrippedWhenNoProvidesPhonemes()
    {
        var processor = CreateProcessor();
        var segment = new AudioSegment(new float[] { 1f }, 24000, new List<Token>());

        _phonemizer
            .ToPhonemesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhonemeResult("", new List<Token>()));

        _session
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(segment));

        await foreach (
            var _ in processor.ProcessAsync(
                _session,
                TtsEngineCapabilities.None,
                "Hello [__EM1__](//)",
                isLastSegment: false,
                CancellationToken.None
            )
        ) { }

        _session
            .Received(1)
            .SynthesizeAsync(
                Arg.Is<string>(s => !s.Contains("[__EM")),
                false,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ProcessAsync_SetsUniqueSentenceIdOnSegments()
    {
        var processor = CreateProcessor();
        var segment1 = new AudioSegment(new float[] { 1f }, 24000, new List<Token>());
        var segment2 = new AudioSegment(new float[] { 2f }, 24000, new List<Token>());

        _session
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(segment1, segment2));

        var results = new List<AudioSegment>();
        await foreach (
            var s in processor.ProcessAsync(
                _session,
                TtsEngineCapabilities.ProvidesPhonemes,
                "Hello world",
                isLastSegment: false,
                CancellationToken.None
            )
        )
        {
            results.Add(s);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal(results[0].SentenceId, results[1].SentenceId);
        Assert.NotEqual(Guid.Empty, results[0].SentenceId);
    }

    [Fact]
    public async Task ProcessAsync_AudioFilterApplied()
    {
        var audioFilter = Substitute.For<IAudioFilter>();
        audioFilter.Priority.Returns(1);

        var processor = CreateProcessor(audioFilters: [audioFilter]);
        var segment = new AudioSegment(new float[] { 1f }, 24000, new List<Token>());

        _session
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(segment));

        await foreach (
            var _ in processor.ProcessAsync(
                _session,
                TtsEngineCapabilities.ProvidesPhonemes,
                "Hello",
                isLastSegment: false,
                CancellationToken.None
            )
        ) { }

        audioFilter.Received().Process(Arg.Any<AudioSegment>());
    }

    private static async IAsyncEnumerable<AudioSegment> ToAsyncEnumerable(
        params AudioSegment[] segments
    )
    {
        foreach (var s in segments)
        {
            yield return s;
        }

        await Task.CompletedTask;
    }
}
