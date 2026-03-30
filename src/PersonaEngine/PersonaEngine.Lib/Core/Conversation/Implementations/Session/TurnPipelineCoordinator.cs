using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

internal sealed class TurnPipelineCoordinator : IAsyncDisposable
{
    private readonly IChatEngine _chatEngine;
    private readonly ILogger _logger;
    private readonly IOutputAdapter _outputAdapter;
    private readonly ITtsEngine _ttsEngine;

    private Task? _audioTask;
    private Channel<LlmChunkEvent>? _llmChannel;
    private Task? _llmTask;
    private Channel<TtsChunkEvent>? _ttsChannel;
    private Task? _ttsTask;
    private CancellationTokenSource? _turnCts;

    public TurnPipelineCoordinator(
        IChatEngine chatEngine,
        ITtsEngine ttsEngine,
        IOutputAdapter outputAdapter,
        ILogger logger
    )
    {
        _chatEngine = chatEngine;
        _ttsEngine = ttsEngine;
        _outputAdapter = outputAdapter;
        _logger = logger;
    }

    public Guid? CurrentTurnId { get; private set; }

    public CancellationToken? TurnCancellationToken => _turnCts?.Token;

    public bool IsAudioOutput => _outputAdapter is IAudioOutputAdapter;

    public bool HasValidTurn => CurrentTurnId.HasValue && _turnCts is not null;

    public bool HasLlmChannel => _llmChannel is not null;

    public async ValueTask DisposeAsync()
    {
        await CancelCurrentAsync();
    }

    public Guid StartNewTurn(CancellationToken sessionToken)
    {
        _turnCts = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        CurrentTurnId = Guid.NewGuid();

        return CurrentTurnId.Value;
    }

    public async ValueTask CancelCurrentAsync()
    {
        if (_turnCts == null)
        {
            return;
        }

        var turnIdBeingCancelled = CurrentTurnId;

        _logger.LogDebug(
            "Requesting cancellation for Turn {TurnId}'s processing pipeline.",
            turnIdBeingCancelled
        );

        if (_turnCts is { IsCancellationRequested: false })
        {
            try
            {
                await _turnCts.CancelAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error cancelling CancellationTokenSource for Turn {TurnId}.",
                    turnIdBeingCancelled
                );
            }
        }

        if (turnIdBeingCancelled.HasValue)
        {
            await WaitForTaskCancellationAsync(
                _llmTask,
                "LLM response generation",
                turnIdBeingCancelled.Value
            );
            await WaitForTaskCancellationAsync(
                _ttsTask,
                "TTS generation",
                turnIdBeingCancelled.Value
            );
            await WaitForTaskCancellationAsync(
                _audioTask,
                "Audio processing",
                turnIdBeingCancelled.Value
            );
        }

        _turnCts?.Dispose();
        _turnCts = null;
        CurrentTurnId = null;
        _llmTask = null;
        _ttsTask = null;
        _audioTask = null;

        _llmChannel?.Writer.TryComplete(new OperationCanceledException());
        _ttsChannel?.Writer.TryComplete(new OperationCanceledException());

        _llmChannel = null;
        _ttsChannel = null;
    }

    public void StartLlmStage(
        IConversationContext context,
        ChannelWriter<IOutputEvent> outputWriter,
        Guid sessionId
    )
    {
        var turnId = CurrentTurnId;
        var turnCts = _turnCts;

        if (!turnId.HasValue || turnCts is null)
        {
            _logger.LogError(
                "StartLlmStage called in invalid state (TurnId: {TurnId}, Cts: {Cts}).",
                turnId,
                turnCts != null
            );

            throw new InvalidOperationException("Cannot start LLM stream in invalid state.");
        }

        _llmChannel = Channel.CreateUnbounded<LlmChunkEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
        );

        _llmTask = _chatEngine.GetStreamingChatResponseAsync(
            context,
            outputWriter,
            turnId.Value,
            sessionId,
            cancellationToken: turnCts.Token
        );
    }

    public void StartTtsStage(ChannelWriter<IOutputEvent> outputWriter, Guid sessionId)
    {
        var turnId = CurrentTurnId;
        var turnCts = _turnCts;

        if (!turnId.HasValue || turnCts is null || _llmChannel is null)
        {
            _logger.LogWarning("StartTtsStage called without a valid TurnId or LLM channel.");

            return;
        }

        _ttsChannel = Channel.CreateUnbounded<TtsChunkEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
        );

        _ttsTask = _ttsEngine.SynthesizeStreamingAsync(
            _llmChannel,
            outputWriter,
            turnId.Value,
            sessionId,
            cancellationToken: turnCts.Token
        );

        if (_outputAdapter is IAudioOutputAdapter audioOutput)
        {
            _audioTask = audioOutput.SendAsync(
                _ttsChannel,
                outputWriter,
                turnId.Value,
                turnCts.Token
            );
        }
    }

    public async ValueTask WriteLlmChunkAsync(
        LlmChunkEvent chunk,
        CancellationToken cancellationToken
    )
    {
        var writer = _llmChannel?.Writer;

        if (writer is null)
        {
            _logger.LogWarning("WriteLlmChunkAsync called without a valid LLM channel.");

            return;
        }

        await writer.WriteAsync(chunk, cancellationToken);
    }

    public async ValueTask WriteTtsChunkAsync(
        TtsChunkEvent chunk,
        CancellationToken cancellationToken
    )
    {
        var writer = _ttsChannel?.Writer;

        if (writer is null)
        {
            _logger.LogWarning("WriteTtsChunkAsync called without a valid TTS channel.");

            return;
        }

        await writer.WriteAsync(chunk, cancellationToken);
    }

    public void CompleteLlmChannel()
    {
        _llmChannel?.Writer.TryComplete();
    }

    public void CompleteTtsChannel()
    {
        _ttsChannel?.Writer.TryComplete();
    }

    public void TryCompleteAllChannels()
    {
        _llmChannel?.Writer.TryComplete();
        _ttsChannel?.Writer.TryComplete();
        _llmChannel = null;
        _ttsChannel = null;
    }

    private async Task WaitForTaskCancellationAsync(
        Task? task,
        string taskName,
        Guid turnIdBeingCancelled
    )
    {
        if (task is { IsCompleted: false })
        {
            _logger.LogTrace(
                "Waiting briefly for {TaskName} task cancellation acknowledgment (Turn {TurnId}).",
                taskName,
                turnIdBeingCancelled
            );

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                /* Ignored */
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Exception while waiting for {TaskName} task cancellation (Turn {TurnId}). Task Status: {Status}",
                    taskName,
                    turnIdBeingCancelled,
                    task.Status
                );
            }
        }
        else
        {
            _logger.LogTrace(
                "{TaskName} task was null or already completed (Turn {TurnId}). No wait needed.",
                taskName,
                turnIdBeingCancelled
            );
        }
    }
}
