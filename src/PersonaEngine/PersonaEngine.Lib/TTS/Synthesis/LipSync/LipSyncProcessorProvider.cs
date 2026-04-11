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
    private readonly IReadOnlyDictionary<string, ILipSyncProcessor> _processors;
    private readonly ILogger<LipSyncProcessorProvider> _logger;
    private readonly IDisposable? _changeToken;

    private volatile ILipSyncProcessor _current;

    public LipSyncProcessorProvider(
        IEnumerable<ILipSyncProcessor> processors,
        IOptionsMonitor<LipSyncOptions> config,
        ILogger<LipSyncProcessorProvider> logger
    )
    {
        _logger = logger;
        _processors = processors.ToDictionary(
            p => p.EngineId,
            p => p,
            StringComparer.OrdinalIgnoreCase
        );

        _current = Resolve(config.CurrentValue.Engine);
        _changeToken = config.OnChange(cfg =>
        {
            var newProcessor = Resolve(cfg.Engine);
            if (
                string.Equals(
                    newProcessor.EngineId,
                    _current.EngineId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
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

    private ILipSyncProcessor Resolve(string engineId)
    {
        if (_processors.TryGetValue(engineId, out var processor))
        {
            return processor;
        }

        _logger.LogError(
            "LipSync processor '{EngineId}' not registered. Available: {Available}. Falling back to first registered processor.",
            engineId,
            string.Join(", ", _processors.Keys)
        );

        return _processors.Values.First();
    }
}
