using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.TTS.Synthesis.Engine;

/// <summary>
///     Resolves <see cref="ISentenceSynthesizer" /> by matching
///     <see cref="TtsConfiguration.ActiveEngine" /> against registered synthesizers' EngineId.
///     Thread-safe for runtime switching via <see cref="IOptionsMonitor{TOptions}" />.
/// </summary>
internal sealed class TtsEngineProvider : ITtsEngineProvider, IDisposable
{
    private readonly IReadOnlyDictionary<string, ISentenceSynthesizer> _engines;
    private readonly ILogger<TtsEngineProvider> _logger;
    private readonly IDisposable? _changeToken;
    private readonly ConcurrentDictionary<string, byte> _warnedMissing = new(
        StringComparer.OrdinalIgnoreCase
    );

    private volatile ISentenceSynthesizer _current;

    public TtsEngineProvider(
        IEnumerable<ISentenceSynthesizer> engines,
        IOptionsMonitor<TtsConfiguration> config,
        ILogger<TtsEngineProvider> logger
    )
    {
        _logger = logger;
        _engines = engines.ToDictionary(e => e.EngineId, e => e, StringComparer.OrdinalIgnoreCase);

        AvailableEngines = _engines
            .Values.Select(e => new TtsEngineInfo(e.EngineId, e.Capabilities))
            .ToList();

        _current = Resolve(config.CurrentValue.ActiveEngine);
        _changeToken = config.OnChange(cfg =>
        {
            var newEngine = Resolve(cfg.ActiveEngine);
            _current = newEngine;
            _logger.LogInformation("TTS engine switched to '{EngineId}'.", newEngine.EngineId);
        });
    }

    public ISentenceSynthesizer Current => _current;

    public IReadOnlyList<TtsEngineInfo> AvailableEngines { get; }

    public ISentenceSynthesizer Get(string engineId)
    {
        if (_engines.TryGetValue(engineId, out var engine))
        {
            return engine;
        }

        throw new KeyNotFoundException($"No TTS engine registered with id '{engineId}'.");
    }

    public void Dispose()
    {
        _changeToken?.Dispose();
    }

    private ISentenceSynthesizer Resolve(string engineId)
    {
        if (_engines.TryGetValue(engineId, out var engine))
        {
            return engine;
        }

        var fallback = _engines.Values.First();

        // Dedupe so a missing optional engine (e.g. qwen3 on the TryItOut
        // profile) doesn't spam the log on every config-monitor callback.
        if (_warnedMissing.TryAdd(engineId ?? string.Empty, 0))
        {
            _logger.LogWarning(
                "TTS engine '{EngineId}' is not installed in this profile. Available: {Available}. Using '{Fallback}' instead.",
                engineId,
                string.Join(", ", _engines.Keys),
                fallback.EngineId
            );
        }

        return fallback;
    }
}
