using NSubstitute;
using PersonaEngine.Lib.ASR.VAD;
using Xunit;

namespace PersonaEngine.Lib.Tests.ASR.VAD;

public class VadProbabilityProviderTests
{
    [Fact]
    public void CurrentProbability_Initially_IsZero()
    {
        var detector = Substitute.For<IVadDetector>();
        var provider = new VadProbabilityProvider(detector);
        Assert.Equal(0f, provider.CurrentProbability);
    }

    [Fact]
    public void OnProbabilityObserved_UpdatesCurrent()
    {
        var detector = Substitute.For<IVadDetector>();
        var provider = new VadProbabilityProvider(detector);

        detector.ProbabilityObserved += Raise.Event<Action<float>>(0.42f);

        Assert.Equal(0.42f, provider.CurrentProbability);
    }

    [Fact]
    public void OnProbabilityObserved_AdvancesHistoryHead()
    {
        var detector = Substitute.For<IVadDetector>();
        var provider = new VadProbabilityProvider(detector);
        var startHead = provider.HistoryHead;

        detector.ProbabilityObserved += Raise.Event<Action<float>>(0.1f);

        Assert.NotEqual(startHead, provider.HistoryHead);
    }

    [Fact]
    public void OnProbabilityObserved_WritesValue_IntoHistory()
    {
        var detector = Substitute.For<IVadDetector>();
        var provider = new VadProbabilityProvider(detector);

        detector.ProbabilityObserved += Raise.Event<Action<float>>(0.7f);

        var previousSlot =
            (provider.HistoryHead - 1 + provider.History.Length) % provider.History.Length;
        Assert.Equal(0.7f, provider.History[previousSlot]);
    }

    [Fact]
    public void Dispose_UnsubscribesFromEvent()
    {
        var detector = Substitute.For<IVadDetector>();
        var provider = new VadProbabilityProvider(detector);
        provider.Dispose();

        // After Dispose, subsequent raises should not update state
        detector.ProbabilityObserved += Raise.Event<Action<float>>(0.9f);
        Assert.Equal(0f, provider.CurrentProbability);
    }
}
