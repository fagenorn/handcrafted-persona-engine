using PersonaEngine.Lib.Utils;

namespace PersonaEngine.Lib.ASR.VAD;

/// <summary>
///     Subscribes to <see cref="IVadDetector.ProbabilityObserved" /> and exposes the latest
///     value plus a trailing history ring buffer for UI consumers.
/// </summary>
public sealed class VadProbabilityProvider : IVadProbabilityProvider, IDisposable
{
    private const int HistoryCapacity = 64;

    private readonly IVadDetector _detector;
    private FloatRingBuffer _history = new(HistoryCapacity);
    private float _current;

    public VadProbabilityProvider(IVadDetector detector)
    {
        _detector = detector;
        _detector.ProbabilityObserved += OnProbabilityObserved;
    }

    public float CurrentProbability => _current;

    public ReadOnlySpan<float> History => _history.Values;

    public int HistoryHead => _history.Head;

    public void Dispose() => _detector.ProbabilityObserved -= OnProbabilityObserved;

    private void OnProbabilityObserved(float probability)
    {
        _current = probability;
        _history.Push(probability);
    }
}
