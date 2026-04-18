using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.IO;
using PersonaEngine.Lib.TTS.Synthesis.Alignment;
using PersonaEngine.Lib.TTS.Synthesis.Engine;

namespace PersonaEngine.Lib.TTS.Synthesis.Qwen3;

/// <summary>
///     Qwen3-TTS sentence synthesizer. Lazily initializes the underlying
///     <see cref="Qwen3TtsGgufEngine" /> on first use, and creates per-turn
///     <see cref="Qwen3SynthesisSession" /> instances that carry decoder state
///     across sentences for audio continuity.
/// </summary>
internal sealed class Qwen3SentenceSynthesizer : ISentenceSynthesizer
{
    private readonly ILogger<Qwen3SentenceSynthesizer> _logger;
    private readonly IOptionsMonitor<Qwen3TtsOptions> _options;
    private readonly IModelProvider _modelProvider;
    private readonly IForcedAligner _aligner;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private Qwen3TtsGgufEngine? _engine;
    private bool _disposed;

    public Qwen3SentenceSynthesizer(
        IOptionsMonitor<Qwen3TtsOptions> options,
        IModelProvider modelProvider,
        IForcedAligner aligner,
        ILogger<Qwen3SentenceSynthesizer> logger
    )
    {
        _options = options;
        _modelProvider = modelProvider;
        _aligner = aligner;
        _logger = logger;
    }

    public string EngineId => "qwen3";

    public TtsEngineCapabilities Capabilities => TtsEngineCapabilities.VoiceInstruction;

    public ISynthesisSession CreateSession()
    {
        var engine = EnsureInitialized();
        return new Qwen3SynthesisSession(engine, _options.CurrentValue);
    }

    public ISynthesisSession CreateSession(string voiceName)
    {
        var engine = EnsureInitialized();
        var opts = _options.CurrentValue with { Speaker = voiceName };
        return new Qwen3SynthesisSession(engine, opts);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _engine?.Dispose();
        _initLock.Dispose();

        return ValueTask.CompletedTask;
    }

    private Qwen3TtsGgufEngine EnsureInitialized()
    {
        if (_engine is not null)
        {
            return _engine;
        }

        _initLock.Wait();
        try
        {
            if (_engine is not null)
            {
                return _engine;
            }

            _logger.LogInformation("Loading Qwen3-TTS engine (GGUF models + ONNX decoder)...");

            _engine = Qwen3TtsGgufEngine.Load(_modelProvider, _aligner, _logger);

            _logger.LogInformation("Qwen3-TTS engine loaded successfully.");

            return _engine;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load Qwen3-TTS engine. TTS will not work until the issue is resolved."
            );

            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
