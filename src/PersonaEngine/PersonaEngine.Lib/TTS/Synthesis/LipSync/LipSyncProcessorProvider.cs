using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.TTS.Synthesis.LipSync;

/// <summary>
///     Resolves <see cref="ILipSyncProcessor" /> by matching
///     <see cref="LipSyncOptions.Engine" /> against registered processors' EngineId.
///     Thread-safe for runtime switching via <see cref="IOptionsMonitor{TOptions}" />.
/// </summary>
internal sealed class LipSyncProcessorProvider : ILipSyncProcessorProvider, IDisposable
{
    private readonly IReadOnlyDictionary<LipSyncEngine, ILipSyncProcessor> _processors;
    private readonly ILogger<LipSyncProcessorProvider> _logger;
    private readonly IDisposable? _changeToken;
    private readonly ConcurrentDictionary<LipSyncEngine, byte> _warnedMissing = new();

    private volatile ILipSyncProcessor _current;

    public LipSyncProcessorProvider(
        IEnumerable<ILipSyncProcessor> processors,
        IOptionsMonitor<LipSyncOptions> config,
        ILogger<LipSyncProcessorProvider> logger
    )
    {
        _logger = logger;
        _processors = processors.ToDictionary(p => p.EngineId, p => p);

        _current = Resolve(config.CurrentValue.Engine);
        _changeToken = config.OnChange(cfg =>
        {
            var newProcessor = Resolve(cfg.Engine);
            if (newProcessor.EngineId == _current.EngineId)
            {
                return;
            }

            newProcessor.Reset();
            _current = newProcessor;
            _logger.LogInformation(
                "LipSync processor switched to '{EngineId}'.",
                newProcessor.EngineId
            );
        });
    }

    public ILipSyncProcessor Current => _current;

    public void Dispose()
    {
        _changeToken?.Dispose();
    }

    private ILipSyncProcessor Resolve(LipSyncEngine engineId)
    {
        if (_processors.TryGetValue(engineId, out var processor))
        {
            return processor;
        }

        var fallback = _processors.Values.First();

        // Dedupe: a missing optional processor (e.g. Audio2Face on TryItOut)
        // shouldn't spam the log on every config-monitor callback.
        if (_warnedMissing.TryAdd(engineId, 0))
        {
            _logger.LogWarning(
                "LipSync processor '{EngineId}' is not installed in this profile. Available: {Available}. Using '{Fallback}' instead.",
                engineId,
                string.Join(", ", _processors.Keys),
                fallback.EngineId
            );
        }

        return fallback;
    }
}
