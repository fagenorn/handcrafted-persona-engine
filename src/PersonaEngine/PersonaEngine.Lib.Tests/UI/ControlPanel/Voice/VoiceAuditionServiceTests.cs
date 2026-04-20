using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.TTS.RVC;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Engine;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;
using Xunit;

namespace PersonaEngine.Lib.Tests.UI.ControlPanel.Voice;

public sealed class VoiceAuditionServiceTests
{
    [Fact]
    public async Task Preview_Resolves_Engine_By_Id_And_Creates_Session()
    {
        var (provider, synthesizer, _, rvc, player, tts, phonemizer) = BuildSubs();
        provider.Get("kokoro").Returns(synthesizer);

        var svc = new VoiceAuditionService(
            provider,
            rvc,
            player,
            phonemizer,
            tts,
            NullLogger<VoiceAuditionService>.Instance
        );

        await svc.PreviewAsync(
            new VoiceAuditionRequest
            {
                Id = "test",
                Engine = "kokoro",
                Voice = "af_heart",
            }
        );

        provider.Received(1).Get("kokoro");
        synthesizer.Received(1).CreateSession("af_heart");
    }

    [Fact]
    public async Task Rvc_Applied_Only_When_Enabled()
    {
        var (provider, synthesizer, _, rvc, player, tts, phonemizer) = BuildSubs();
        provider.Get(Arg.Any<string>()).Returns(synthesizer);

        var svc = new VoiceAuditionService(
            provider,
            rvc,
            player,
            phonemizer,
            tts,
            NullLogger<VoiceAuditionService>.Instance
        );

        await svc.PreviewAsync(
            new VoiceAuditionRequest
            {
                Id = "test_dry",
                Engine = "kokoro",
                Voice = "af_heart",
                RvcEnabled = false,
            }
        );
        rvc.DidNotReceive().Apply(Arg.Any<AudioSegment>(), Arg.Any<RvcOverride>());

        await svc.PreviewAsync(
            new VoiceAuditionRequest
            {
                Id = "test_wet",
                Engine = "kokoro",
                Voice = "af_heart",
                RvcEnabled = true,
                RvcVoice = "KasumiVA",
            }
        );
        rvc.Received().Apply(Arg.Any<AudioSegment>(), Arg.Any<RvcOverride>());
    }

    [Fact]
    public async Task Second_Preview_Cancels_First_And_IsPreviewing_Transitions_Back()
    {
        var (provider, synthesizer, _, rvc, player, tts, phonemizer) = BuildSubs();
        provider.Get(Arg.Any<string>()).Returns(synthesizer);

        var svc = new VoiceAuditionService(
            provider,
            rvc,
            player,
            phonemizer,
            tts,
            NullLogger<VoiceAuditionService>.Instance
        );

        Assert.False(svc.IsPreviewing);

        var first = svc.PreviewAsync(
            new VoiceAuditionRequest
            {
                Id = "test",
                Engine = "kokoro",
                Voice = "af_heart",
            }
        );
        var second = svc.PreviewAsync(
            new VoiceAuditionRequest
            {
                Id = "test2",
                Engine = "kokoro",
                Voice = "af_bella",
            }
        );

        await first;
        await second;

        Assert.False(svc.IsPreviewing);
    }

    private static (
        ITtsEngineProvider provider,
        ISentenceSynthesizer synthesizer,
        ISynthesisSession session,
        IRvcAuditionProcessor rvc,
        IOneShotPlayer player,
        IOptionsMonitor<TtsConfiguration> tts,
        IPhonemizer phonemizer
    ) BuildSubs()
    {
        var provider = Substitute.For<ITtsEngineProvider>();
        var synthesizer = Substitute.For<ISentenceSynthesizer>();
        var session = Substitute.For<ISynthesisSession>();

        synthesizer.CreateSession().Returns(session);
        synthesizer.CreateSession(Arg.Any<string>()).Returns(session);

        session
            .SynthesizeAsync(
                Arg.Any<string>(),
                Arg.Any<PhonemeResult>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => SingleSegmentAsync());

        var rvc = Substitute.For<IRvcAuditionProcessor>();
        var player = Substitute.For<IOneShotPlayer>();
        var tts = Substitute.For<IOptionsMonitor<TtsConfiguration>>();
        tts.CurrentValue.Returns(new TtsConfiguration());

        var phonemizer = Substitute.For<IPhonemizer>();
        phonemizer
            .ToPhonemesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(PhonemeResult.Empty);

        return (provider, synthesizer, session, rvc, player, tts, phonemizer);
    }

    private static async IAsyncEnumerable<AudioSegment> SingleSegmentAsync()
    {
        yield return new AudioSegment(new float[1024], 24000, Array.Empty<Token>());
        await Task.CompletedTask;
    }
}
