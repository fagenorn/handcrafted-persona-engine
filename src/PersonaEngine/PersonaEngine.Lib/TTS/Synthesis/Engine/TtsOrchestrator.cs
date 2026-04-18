using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.TTS.Synthesis.TextProcessing;

namespace PersonaEngine.Lib.TTS.Synthesis.Engine;

/// <summary>
///     Engine-agnostic TTS orchestrator. Reads LLM chunks, segments sentences
///     incrementally, delegates per-sentence synthesis to <see cref="SentenceProcessor" />,
///     and emits TTS lifecycle events via <see cref="TtsEventEmitter" />.
/// </summary>
public sealed class TtsOrchestrator : ITtsEngine
{
    private readonly ITtsEngineProvider _engineProvider;
    private readonly SentenceProcessor _sentenceProcessor;
    private readonly ITextNormalizer _normalizer;
    private readonly ISentenceSegmenter _segmenter;
    private readonly ILogger<TtsOrchestrator> _logger;

    private readonly object _readinessGate = new();
    private bool _isReady = true;
    private string? _lastInitError;

    public TtsOrchestrator(
        ITtsEngineProvider engineProvider,
        SentenceProcessor sentenceProcessor,
        ITextNormalizer normalizer,
        ISentenceSegmenter segmenter,
        ILoggerFactory loggerFactory
    )
    {
        _engineProvider = engineProvider ?? throw new ArgumentNullException(nameof(engineProvider));
        _sentenceProcessor =
            sentenceProcessor ?? throw new ArgumentNullException(nameof(sentenceProcessor));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _segmenter = segmenter ?? throw new ArgumentNullException(nameof(segmenter));
        _logger =
            loggerFactory.CreateLogger<TtsOrchestrator>()
            ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public bool IsReady
    {
        get
        {
            lock (_readinessGate)
            {
                return _isReady;
            }
        }
    }

    public string? LastInitError
    {
        get
        {
            lock (_readinessGate)
            {
                return _lastInitError;
            }
        }
    }

    public event Action? ReadyChanged;

    public void Dispose() { }

    public async Task<CompletionReason> SynthesizeStreamingAsync(
        ChannelReader<LlmChunkEvent> inputReader,
        ChannelWriter<IOutputEvent> outputWriter,
        Guid turnId,
        Guid sessionId,
        CancellationToken cancellationToken = default
    )
    {
        var synthesizer = _engineProvider.Current;
        var emitter = new TtsEventEmitter(outputWriter, sessionId, turnId);
        var accumulator = new IncrementalSentenceAccumulator(_normalizer, _segmenter);

        _logger.LogDebug(
            "TTS orchestrator: starting with engine '{EngineId}' for turn {TurnId}",
            synthesizer.EngineId,
            turnId
        );

        ISynthesisSession? session = null;
        try
        {
            try
            {
                session = synthesizer.CreateSession();
            }
            catch (Exception ex)
            {
                UpdateReadiness(isReady: false, error: ex.Message);
                throw;
            }

            UpdateReadiness(isReady: true, error: null);

            await foreach (
                var (_, _, _, textChunk) in inputReader
                    .ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                if (string.IsNullOrEmpty(textChunk))
                {
                    continue;
                }

                accumulator.Append(textChunk);

                foreach (var sentence in accumulator.TakeCompletedSentences())
                {
                    await emitter.EmitReadyToSynthesizeAsync().ConfigureAwait(false);

                    await foreach (
                        var segment in _sentenceProcessor.ProcessAsync(
                            session,
                            sentence,
                            isLastSegment: false,
                            cancellationToken
                        )
                    )
                    {
                        await emitter.EmitChunkAsync(segment).ConfigureAwait(false);
                    }
                }
            }

            var remaining = accumulator.Flush();
            if (remaining is not null)
            {
                await emitter.EmitReadyToSynthesizeAsync().ConfigureAwait(false);

                await foreach (
                    var segment in _sentenceProcessor.ProcessAsync(
                        session,
                        remaining,
                        isLastSegment: true,
                        cancellationToken
                    )
                )
                {
                    await emitter.EmitChunkAsync(segment).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            emitter.SetReason(CompletionReason.Cancelled);
            _logger.LogDebug("TTS orchestrator: cancelled for turn {TurnId}", turnId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "TTS orchestrator: error during synthesis for turn {TurnId} (engine '{EngineId}')",
                turnId,
                synthesizer.EngineId
            );
            await emitter.EmitErrorAsync(ex).ConfigureAwait(false);
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            await emitter.EmitStreamEndAsync().ConfigureAwait(false);
        }

        _logger.LogDebug(
            "TTS orchestrator: finished with result {Result} for turn {TurnId}",
            emitter.CompletionReason,
            turnId
        );

        return emitter.CompletionReason;
    }

    private void UpdateReadiness(bool isReady, string? error)
    {
        bool changed;
        lock (_readinessGate)
        {
            changed = _isReady != isReady || _lastInitError != error;
            if (changed)
            {
                _isReady = isReady;
                _lastInitError = error;
            }
        }

        if (changed)
        {
            ReadyChanged?.Invoke();
        }
    }
}
