# ConversationSession SRP Refactor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decompose the 1427-line `ConversationSession` class into focused collaborator types (`TurnMetricsTracker`, `TurnPipelineCoordinator`) and partial class files for SRP and maintainability, with zero behavioral changes and zero unnecessary allocations.

**Architecture:** Extract two collaborator types that `ConversationSession` delegates to: a `TurnMetricsTracker` struct for all timing/latency state, and a `TurnPipelineCoordinator` class for LLM→TTS→Audio task/channel lifecycle. Reorganize the remaining session code into partial class files by responsibility. Fix the `ProcessOutputEventsAsync` channel completion bug.

**Tech Stack:** .NET 9.0, C#, Stateless (state machine), System.Threading.Channels, System.Diagnostics (Stopwatch), CSharpier formatting

---

## File Structure

```
Core/Conversation/Implementations/Session/
  ConversationSession.cs                        # Fields, ctor, lifecycle (Run/Stop/Dispose)         ~150 lines
  ConversationSession.ConfigureStateMachine.cs  # State machine config (unchanged)                   ~200 lines
  ConversationSession.EventProcessing.cs        # Input/output loops + event mapping (NEW partial)   ~120 lines
  ConversationSession.StateActions.cs           # Handle*/Prepare*/Cleanup* methods (NEW partial)    ~250 lines
  TurnMetricsTracker.cs                         # NEW struct — timing/latency/completion              ~180 lines
  TurnPipelineCoordinator.cs                    # NEW class — pipeline task/channel lifecycle          ~200 lines
```

**Files modified:**
- `ConversationSession.cs` — rewrite to slim core (fields, ctor, lifecycle only)
- `ConfigureStateMachine.cs` — update `_currentTurnId` references to `_pipeline.CurrentTurnId`
- `ConversationSessionFactory.cs` — no changes needed (constructor signature unchanged)

**Files created:**
- `TurnMetricsTracker.cs`
- `TurnPipelineCoordinator.cs`
- `ConversationSession.EventProcessing.cs`
- `ConversationSession.StateActions.cs`

---

### Task 1: Create `TurnMetricsTracker` struct

**Files:**
- Create: `src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/TurnMetricsTracker.cs`

- [ ] **Step 1: Create `TurnMetricsTracker.cs` with all timing state and methods**

```csharp
using System.Diagnostics;

using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

internal readonly record struct TurnTimingSummary(
    double? TurnDurationMs,
    double? SttLatencyMs,
    double? FirstLlmTokenMs,
    double? FirstTtsChunkMs,
    double? FirstAudioMs,
    double? LlmDurationMs,
    double? TtsDurationMs,
    double? AudioDurationMs,
    CompletionReason LlmFinishReason,
    CompletionReason TtsFinishReason,
    CompletionReason AudioFinishReason
);

internal struct TurnMetricsTracker
{
    private Stopwatch? _turnStopwatch;
    private Stopwatch? _llmStopwatch;
    private Stopwatch? _ttsStopwatch;
    private Stopwatch? _audioPlaybackStopwatch;
    private Stopwatch? _firstLlmTokenLatencyStopwatch;
    private Stopwatch? _firstTtsChunkLatencyStopwatch;
    private Stopwatch? _firstAudioLatencyStopwatch;

    private double? _firstLlmTokenLatencyMs;
    private double? _firstTtsChunkLatencyMs;
    private double? _firstAudioLatencyMs;
    private DateTimeOffset? _sttStartTime;
    private double? _sttLatencyMs;

    public CompletionReason LlmFinishReason;
    public CompletionReason TtsFinishReason;
    public CompletionReason AudioFinishReason;

    public double? FirstLlmTokenLatencyMs => _firstLlmTokenLatencyMs;
    public double? FirstTtsChunkLatencyMs => _firstTtsChunkLatencyMs;
    public double? FirstAudioLatencyMs => _firstAudioLatencyMs;
    public double? SttLatencyMs => _sttLatencyMs;
    public Stopwatch? LlmStopwatch => _llmStopwatch;
    public Stopwatch? TtsStopwatch => _ttsStopwatch;
    public Stopwatch? AudioPlaybackStopwatch => _audioPlaybackStopwatch;
    public Stopwatch? TurnStopwatch => _turnStopwatch;
    public Stopwatch? FirstAudioLatencyStopwatch => _firstAudioLatencyStopwatch;

    public void StartTurn(double sttProcessingDurationMs)
    {
        _turnStopwatch = Stopwatch.StartNew();
        _firstLlmTokenLatencyStopwatch = Stopwatch.StartNew();
        _sttLatencyMs = sttProcessingDurationMs;

        LlmFinishReason = CompletionReason.Completed;
        TtsFinishReason = CompletionReason.Completed;
        AudioFinishReason = CompletionReason.Completed;

        _firstLlmTokenLatencyMs = null;
        _firstTtsChunkLatencyMs = null;
        _firstAudioLatencyMs = null;
    }

    public void RecordSttStart(DateTimeOffset timestamp)
    {
        _sttStartTime = timestamp;
    }

    public DateTimeOffset? GetSttStartAndClear()
    {
        var start = _sttStartTime;
        _sttStartTime = null;

        return start;
    }

    public bool HasSttStart => _sttStartTime.HasValue;

    /// <summary>
    /// Records first LLM token latency. Stops LLM latency stopwatch, starts TTS latency stopwatch.
    /// Returns the recorded latency in ms, or null if already recorded.
    /// </summary>
    public double? RecordFirstLlmToken()
    {
        if (_firstLlmTokenLatencyStopwatch == null)
        {
            return null;
        }

        _firstLlmTokenLatencyStopwatch.Stop();
        _firstLlmTokenLatencyMs = _firstLlmTokenLatencyStopwatch.Elapsed.TotalMilliseconds;
        _firstLlmTokenLatencyStopwatch = null;
        _firstTtsChunkLatencyStopwatch = Stopwatch.StartNew();

        return _firstLlmTokenLatencyMs;
    }

    /// <summary>
    /// Records first TTS chunk latency. Stops TTS latency stopwatch, starts audio latency stopwatch.
    /// Returns the recorded latency in ms, or null if already recorded.
    /// </summary>
    public double? RecordFirstTtsChunk()
    {
        if (_firstTtsChunkLatencyStopwatch == null)
        {
            return null;
        }

        _firstTtsChunkLatencyStopwatch.Stop();
        _firstTtsChunkLatencyMs = _firstTtsChunkLatencyStopwatch.Elapsed.TotalMilliseconds;
        _firstTtsChunkLatencyStopwatch = null;
        _firstAudioLatencyStopwatch = Stopwatch.StartNew();

        return _firstTtsChunkLatencyMs;
    }

    /// <summary>
    /// Records first audio chunk latency. Stops audio latency stopwatch.
    /// Returns the recorded latency in ms, or null if already recorded.
    /// </summary>
    public double? RecordFirstAudioChunk()
    {
        if (_firstAudioLatencyStopwatch == null)
        {
            return null;
        }

        _firstAudioLatencyStopwatch.Stop();
        _firstAudioLatencyMs = _firstAudioLatencyStopwatch.Elapsed.TotalMilliseconds;
        _firstAudioLatencyStopwatch = null;

        return _firstAudioLatencyMs;
    }

    public void StartLlmStopwatch()
    {
        _llmStopwatch = Stopwatch.StartNew();
    }

    public double? StopLlmStopwatch()
    {
        if (_llmStopwatch == null)
        {
            return null;
        }

        _llmStopwatch.Stop();
        var duration = _llmStopwatch.Elapsed.TotalMilliseconds;
        _llmStopwatch = null;

        return duration;
    }

    public void StartTtsStopwatch()
    {
        _ttsStopwatch ??= Stopwatch.StartNew();
    }

    public double? StopTtsStopwatch()
    {
        if (_ttsStopwatch == null)
        {
            return null;
        }

        _ttsStopwatch.Stop();
        var duration = _ttsStopwatch.Elapsed.TotalMilliseconds;
        _ttsStopwatch = null;

        return duration;
    }

    public void StartAudioPlaybackStopwatch()
    {
        _audioPlaybackStopwatch = Stopwatch.StartNew();
    }

    public double? StopAudioPlaybackStopwatch()
    {
        if (_audioPlaybackStopwatch == null)
        {
            return null;
        }

        _audioPlaybackStopwatch.Stop();
        var duration = _audioPlaybackStopwatch.Elapsed.TotalMilliseconds;
        _audioPlaybackStopwatch = null;

        return duration;
    }

    public double? StopTurnStopwatch()
    {
        if (_turnStopwatch == null)
        {
            return null;
        }

        _turnStopwatch.Stop();

        return _turnStopwatch.Elapsed.TotalMilliseconds;
    }

    public void ClearNonAudioTtsTracking()
    {
        _firstTtsChunkLatencyStopwatch?.Stop();
        _firstTtsChunkLatencyStopwatch = null;
        _firstTtsChunkLatencyMs = null;
    }

    public TurnTimingSummary CompleteTurn()
    {
        var summary = new TurnTimingSummary(
            _turnStopwatch?.Elapsed.TotalMilliseconds,
            _sttLatencyMs,
            _firstLlmTokenLatencyMs,
            _firstTtsChunkLatencyMs,
            _firstAudioLatencyMs,
            _llmStopwatch?.Elapsed.TotalMilliseconds,
            _ttsStopwatch?.Elapsed.TotalMilliseconds,
            _audioPlaybackStopwatch?.Elapsed.TotalMilliseconds,
            LlmFinishReason,
            TtsFinishReason,
            AudioFinishReason
        );

        Reset();

        return summary;
    }

    public void Reset()
    {
        _turnStopwatch?.Stop();
        _llmStopwatch?.Stop();
        _ttsStopwatch?.Stop();
        _audioPlaybackStopwatch?.Stop();
        _firstAudioLatencyStopwatch?.Stop();
        _firstLlmTokenLatencyStopwatch?.Stop();
        _firstTtsChunkLatencyStopwatch?.Stop();

        _turnStopwatch = null;
        _llmStopwatch = null;
        _ttsStopwatch = null;
        _audioPlaybackStopwatch = null;
        _firstAudioLatencyStopwatch = null;
        _firstLlmTokenLatencyStopwatch = null;
        _firstTtsChunkLatencyStopwatch = null;
        _firstLlmTokenLatencyMs = null;
        _firstTtsChunkLatencyMs = null;
        _firstAudioLatencyMs = null;
        _sttStartTime = null;
        _sttLatencyMs = null;

        LlmFinishReason = CompletionReason.Completed;
        TtsFinishReason = CompletionReason.Completed;
        AudioFinishReason = CompletionReason.Completed;
    }
}
```

- [ ] **Step 2: Verify build compiles**

Run: `$HOME/.dotnet/dotnet build src/PersonaEngine/PersonaEngine.sln`
Expected: Build succeeded (new file isn't referenced yet, so no errors)

- [ ] **Step 3: Commit**

```bash
git add src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/TurnMetricsTracker.cs
git commit -m "refactor: add TurnMetricsTracker struct for turn timing encapsulation"
```

---

### Task 2: Create `TurnPipelineCoordinator` class

**Files:**
- Create: `src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/TurnPipelineCoordinator.cs`

- [ ] **Step 1: Create `TurnPipelineCoordinator.cs` with pipeline lifecycle management**

```csharp
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

    public void StartTtsStage(ChannelWriter<IOutputEvent> outputWriter)
    {
        var turnId = CurrentTurnId;
        var turnCts = _turnCts;

        if (!turnId.HasValue || turnCts is null || _llmChannel is null)
        {
            _logger.LogWarning(
                "StartTtsStage called without a valid TurnId or LLM channel."
            );

            return;
        }

        _ttsChannel = Channel.CreateUnbounded<TtsChunkEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
        );

        _ttsTask = _ttsEngine.SynthesizeStreamingAsync(
            _llmChannel,
            outputWriter,
            turnId.Value,
            turnId.Value, // sessionId passed as turnId in original — preserved
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

    public async ValueTask WriteLlmChunkAsync(LlmChunkEvent chunk, CancellationToken cancellationToken)
    {
        var writer = _llmChannel?.Writer;

        if (writer is null)
        {
            _logger.LogWarning("WriteLlmChunkAsync called without a valid LLM channel.");

            return;
        }

        await writer.WriteAsync(chunk, cancellationToken);
    }

    public async ValueTask WriteTtsChunkAsync(TtsChunkEvent chunk, CancellationToken cancellationToken)
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

    public bool IsAudioOutput => _outputAdapter is IAudioOutputAdapter;

    public bool HasValidTurn => CurrentTurnId.HasValue && _turnCts is not null;

    public bool HasLlmChannel => _llmChannel is not null;

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
```

- [ ] **Step 2: Verify build compiles**

Run: `$HOME/.dotnet/dotnet build src/PersonaEngine/PersonaEngine.sln`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/TurnPipelineCoordinator.cs
git commit -m "refactor: add TurnPipelineCoordinator for pipeline task/channel lifecycle"
```

---

### Task 3: Create `ConversationSession.EventProcessing.cs` partial

Move `ProcessInputEventsAsync`, `ProcessOutputEventsAsync`, `MapInputEventToTrigger`, and `MapOutputEventToTrigger` from `ConversationSession.cs` into a new partial class file. Integrate with `TurnMetricsTracker` for output loop metrics. Fix the `_inputChannelWriter.TryComplete()` bug in `ProcessOutputEventsAsync`'s `finally` block.

**Files:**
- Create: `src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/ConversationSession.EventProcessing.cs`

- [ ] **Step 1: Create `ConversationSession.EventProcessing.cs`**

This file moves the two event loops and event mapping methods. The output loop's inline metrics tracking is replaced with `_metricsTracker` calls. The `finally` block bug is fixed (`_outputChannelWriter` instead of `_inputChannelWriter`).

```csharp
using System.Threading.Channels;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Input;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public partial class ConversationSession
{
    private async Task ProcessInputEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("{SessionId} | Starting input event processing loop.", SessionId);

        try
        {
            await foreach (
                var inputEvent in _inputChannel
                    .Reader.ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    if (inputEvent is SttSegmentRecognizing && !_metricsTracker.HasSttStart)
                    {
                        _metricsTracker.RecordSttStart(inputEvent.Timestamp);
                    }

                    if (inputEvent is SttSegmentRecognized sttRecognized)
                    {
                        var sttStart = _metricsTracker.GetSttStartAndClear();
                        if (sttStart.HasValue)
                        {
                            var sttDuration = (
                                sttRecognized.Timestamp - sttStart.Value
                            ).TotalMilliseconds;
                            _metrics.RecordSttSegmentDuration(
                                sttDuration,
                                SessionId,
                                sttRecognized.ParticipantId
                            );
                        }
                    }

                    var (trigger, eventArgs) = MapInputEventToTrigger(inputEvent);

                    if (trigger.HasValue)
                    {
                        _logger.LogTrace(
                            "{SessionId} | Input Loop: Firing trigger {Trigger} for event {EventType}",
                            SessionId,
                            trigger.Value,
                            inputEvent.GetType().Name
                        );

                        await _stateMachine.FireAsync(trigger.Value, eventArgs);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "{SessionId} | Input Loop: Received unhandled input event type: {EventType}",
                            SessionId,
                            inputEvent.GetType().Name
                        );
                    }
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "{SessionId} | Input Loop: State machine transition denied in state {State}. Trigger: {Trigger}, Event: {@Event}",
                        SessionId,
                        _stateMachine.State,
                        MapInputEventToTrigger(inputEvent).Trigger,
                        inputEvent
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "{SessionId} | Input Loop: Error processing input event: {EventType}. Firing ErrorOccurred trigger.",
                        SessionId,
                        inputEvent.GetType().Name
                    );
                    if (
                        _stateMachine.State is ConversationState.Error or ConversationState.Ended
                    )
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("{SessionId} | Input event processing loop cancelled.", SessionId);
        }
        catch (ChannelClosedException)
        {
            _logger.LogInformation(
                "{SessionId} | Input channel closed, ending event processing loop.",
                SessionId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{SessionId} | Unhandled exception in input event processing loop.",
                SessionId
            );
            await FireErrorAsync(ex);
        }
        finally
        {
            _logger.LogInformation(
                "{SessionId} | Input event processing loop finished. Final State: {State}",
                SessionId,
                _stateMachine.State
            );
            _inputChannelWriter.TryComplete();
        }
    }

    private async Task ProcessOutputEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("{SessionId} | Starting output event processing loop.", SessionId);

        try
        {
            await foreach (
                var outputEvent in _outputChannel
                    .Reader.ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    switch (outputEvent)
                    {
                        case LlmStreamStartEvent:
                            _metricsTracker.StartLlmStopwatch();

                            break;
                        case LlmStreamEndEvent ev:
                        {
                            if (!_pipeline.IsAudioOutput)
                            {
                                _metricsTracker.LlmFinishReason = ev.FinishReason;
                                var llmDuration = _metricsTracker.StopLlmStopwatch();
                                if (llmDuration.HasValue)
                                {
                                    _metrics.RecordLlmDuration(
                                        llmDuration.Value,
                                        SessionId,
                                        ev.TurnId ?? Guid.Empty,
                                        _metricsTracker.LlmFinishReason
                                    );
                                }
                            }

                            break;
                        }
                        case TtsReadyToSynthesizeEvent ev:
                        {
                            var latency = _metricsTracker.RecordFirstLlmToken();
                            if (latency.HasValue && ev.TurnId.HasValue)
                            {
                                _logger.LogDebug(
                                    "{SessionId} | {TurnId} | ⏱️ | First LLM [{LatencyMs:N0} ms]",
                                    SessionId,
                                    ev.TurnId.Value,
                                    latency.Value
                                );
                            }

                            break;
                        }
                        case TtsStreamStartEvent:
                            _metricsTracker.StartTtsStopwatch();

                            break;
                        case TtsStreamEndEvent ev:
                        {
                            _metricsTracker.TtsFinishReason = ev.FinishReason;
                            var ttsDuration = _metricsTracker.StopTtsStopwatch();
                            if (ttsDuration.HasValue)
                            {
                                _metrics.RecordTtsDuration(
                                    ttsDuration.Value,
                                    SessionId,
                                    ev.TurnId ?? Guid.Empty,
                                    _metricsTracker.TtsFinishReason
                                );
                            }

                            break;
                        }
                        case AudioPlaybackStartedEvent ev:
                        {
                            var audioLatency = _metricsTracker.RecordFirstAudioChunk();
                            if (audioLatency.HasValue && ev.TurnId.HasValue)
                            {
                                _metrics.RecordFirstAudioLatency(
                                    audioLatency.Value,
                                    SessionId,
                                    ev.TurnId.Value
                                );
                            }

                            _metricsTracker.StartAudioPlaybackStopwatch();

                            break;
                        }
                        case AudioPlaybackEndedEvent ev:
                        {
                            _metricsTracker.AudioFinishReason = ev.FinishReason;
                            var audioDuration = _metricsTracker.StopAudioPlaybackStopwatch();
                            if (audioDuration.HasValue)
                            {
                                _metrics.RecordAudioPlaybackDuration(
                                    audioDuration.Value,
                                    SessionId,
                                    ev.TurnId ?? Guid.Empty,
                                    _metricsTracker.AudioFinishReason
                                );
                            }

                            if (ev.TurnId.HasValue)
                            {
                                var turnDuration = _metricsTracker.StopTurnStopwatch();
                                if (turnDuration.HasValue)
                                {
                                    var overallReason = _metricsTracker.AudioFinishReason;
                                    if (
                                        _metricsTracker.TtsFinishReason
                                            == CompletionReason.Error
                                        || _metricsTracker.LlmFinishReason
                                            == CompletionReason.Error
                                    )
                                    {
                                        overallReason = CompletionReason.Error;
                                    }
                                    else if (
                                        _metricsTracker.TtsFinishReason
                                            == CompletionReason.Cancelled
                                        || _metricsTracker.LlmFinishReason
                                            == CompletionReason.Cancelled
                                    )
                                    {
                                        overallReason = CompletionReason.Cancelled;
                                    }

                                    _metrics.RecordTurnDuration(
                                        turnDuration.Value,
                                        SessionId,
                                        ev.TurnId.Value,
                                        overallReason
                                    );
                                }
                            }

                            break;
                        }
                        case ErrorOutputEvent ev:
                            _metrics.IncrementErrors(SessionId, ev.TurnId, ev.Exception);

                            break;
                    }

                    var (trigger, eventArgs) = MapOutputEventToTrigger(outputEvent);

                    if (trigger.HasValue)
                    {
                        _logger.LogTrace(
                            "{SessionId} | Output Loop: Firing trigger {Trigger} for event {EventType}",
                            SessionId,
                            trigger.Value,
                            outputEvent.GetType().Name
                        );

                        await _stateMachine.FireAsync(trigger.Value, eventArgs);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "{SessionId} | Output Loop: State machine transition denied in state {State}. Trigger: {Trigger}, Event: {@Event}",
                        SessionId,
                        _stateMachine.State,
                        MapOutputEventToTrigger(outputEvent).Trigger,
                        outputEvent
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "{SessionId} | Output Loop: Error processing output event: {EventType}. Firing ErrorOccurred trigger.",
                        SessionId,
                        outputEvent.GetType().Name
                    );
                    if (
                        _stateMachine.State is ConversationState.Error or ConversationState.Ended
                    )
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("{SessionId} | Output event processing loop cancelled.", SessionId);
        }
        catch (ChannelClosedException)
        {
            _logger.LogDebug(
                "{SessionId} | Output channel closed, ending output event processing loop.",
                SessionId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{SessionId} | Unhandled exception in output event processing loop.",
                SessionId
            );
            await FireErrorAsync(ex);
        }
        finally
        {
            _logger.LogInformation(
                "{SessionId} | Output event processing loop finished. Final State: {State}",
                SessionId,
                _stateMachine.State
            );
            _outputChannelWriter.TryComplete(); // BUG FIX: was _inputChannelWriter
        }
    }

    private static (ConversationTrigger? Trigger, object? EventArgs) MapInputEventToTrigger(
        IInputEvent inputEvent
    )
    {
        return inputEvent switch
        {
            SttSegmentRecognizing ev => (ConversationTrigger.InputDetected, ev),
            SttSegmentRecognized ev => (ConversationTrigger.InputFinalized, ev),
            _ => (null, null),
        };
    }

    private static (ConversationTrigger? Trigger, object? EventArgs) MapOutputEventToTrigger(
        IOutputEvent outputEvent
    )
    {
        return outputEvent switch
        {
            LlmStreamStartEvent ev => (ConversationTrigger.LlmStreamStarted, ev),
            LlmChunkEvent ev => (ConversationTrigger.LlmStreamChunkReceived, ev),
            LlmStreamEndEvent ev => (ConversationTrigger.LlmStreamEnded, ev),
            TtsStreamStartEvent ev => (ConversationTrigger.TtsStreamStarted, ev),
            TtsChunkEvent ev => (ConversationTrigger.TtsStreamChunkReceived, ev),
            TtsStreamEndEvent ev => (ConversationTrigger.TtsStreamEnded, ev),
            AudioPlaybackStartedEvent ev => (ConversationTrigger.AudioStreamStarted, ev),
            AudioPlaybackEndedEvent ev => (ConversationTrigger.AudioStreamEnded, ev),
            ErrorOutputEvent ev => (ConversationTrigger.ErrorOccurred, ev.Exception),
            _ => (null, null),
        };
    }
}
```

Note: `MapInputEventToTrigger` and `MapOutputEventToTrigger` are changed to `static` — they never referenced `this` in the original code.

- [ ] **Step 2: Commit (will not compile yet — references `_metricsTracker` and `_pipeline` which don't exist on session yet)**

```bash
git add src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/ConversationSession.EventProcessing.cs
git commit -m "refactor: extract event processing loops to partial class file

Fixes bug: ProcessOutputEventsAsync now completes _outputChannelWriter in finally (was _inputChannelWriter)."
```

---

### Task 4: Create `ConversationSession.StateActions.cs` partial

Move all state action methods from `ConversationSession.cs` into a new partial class file. Replace inline metrics/pipeline code with delegation to `_metricsTracker` and `_pipeline`.

**Files:**
- Create: `src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/ConversationSession.StateActions.cs`

- [ ] **Step 1: Create `ConversationSession.StateActions.cs`**

```csharp
using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Input;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;

using Stateless;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public partial class ConversationSession
{
    private async Task InitializeSessionAsync()
    {
        _logger.LogDebug("{SessionId} | Action: InitializeSessionAsync", SessionId);

        try
        {
            _context.TryAddParticipant(ASSISTANT_PARTICIPANT);

            var initInputTasks = _inputAdapters
                .Select(async adapter =>
                {
                    await adapter.InitializeAsync(
                        SessionId,
                        _inputChannelWriter,
                        _sessionCts.Token
                    );
                    await adapter.StartAsync(_sessionCts.Token);
                    _context.TryAddParticipant(adapter.Participant);

                    _logger.LogDebug(
                        "{SessionId} | Input Adapter {AdapterId} initialized and started.",
                        SessionId,
                        adapter.AdapterId
                    );
                })
                .ToList();

            var initOutputTasks = Task.Run(async () =>
            {
                await _outputAdapter.InitializeAsync(SessionId, _sessionCts.Token);
                await _outputAdapter.StartAsync(_sessionCts.Token);
                _logger.LogDebug(
                    "{SessionId} | Output Adapter {AdapterId} initialized and started.",
                    SessionId,
                    _outputAdapter.AdapterId
                );
            });

            await Task.WhenAll(initInputTasks.Concat([initOutputTasks]));

            await _stateMachine.FireAsync(ConversationTrigger.InitializeComplete);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{SessionId} | Error during InitializeSessionAsync.", SessionId);
            await _stateMachine.FireAsync(ConversationTrigger.ErrorOccurred, ex);
        }
    }

    private async Task PrepareLlmRequestAsync(IInputEvent inputEvent)
    {
        if (inputEvent is not SttSegmentRecognized finalizedEvent)
        {
            _logger.LogWarning(
                "{SessionId} | PrepareLlmRequestAsync received unexpected event type: {EventType}",
                SessionId,
                inputEvent.GetType().Name
            );
            await FireErrorAsync(
                new ArgumentException(
                    "Invalid event type for InputFinalized trigger.",
                    nameof(inputEvent)
                ),
                false
            );

            return;
        }

        _logger.LogDebug(
            "{SessionId} | Action: PrepareLlmRequestAsync for input: \"{InputText}\"",
            SessionId,
            finalizedEvent.FinalTranscript
        );

        await CancelCurrentTurnProcessingAsync();

        var turnId = _pipeline.StartNewTurn(_sessionCts.Token);
        _metricsTracker.StartTurn(finalizedEvent.ProcessingDuration.TotalMilliseconds);

        _metrics.IncrementTurnsStarted(SessionId);

        try
        {
            _context.StartTurn(
                turnId,
                [finalizedEvent.ParticipantId, ASSISTANT_PARTICIPANT.Id]
            );
            _context.AppendToTurn(finalizedEvent.ParticipantId, finalizedEvent.FinalTranscript);

            var userName = _context.Participants.TryGetValue(
                finalizedEvent.ParticipantId,
                out var pInfo
            )
                ? pInfo.Name
                : finalizedEvent.ParticipantId;
            _logger.LogInformation(
                "{SessionId} | {TurnId} | 📝 | [{Speaker}]{Text}",
                SessionId,
                turnId,
                userName,
                _context.GetPendingMessageText(finalizedEvent.ParticipantId)
            );

            _context.CompleteTurnPart(finalizedEvent.ParticipantId);

            await _stateMachine.FireAsync(ConversationTrigger.LlmRequestSent);

            if (_pipeline.IsAudioOutput)
            {
                await _stateMachine.FireAsync(ConversationTrigger.TtsRequestSent);
            }
            else
            {
                _metricsTracker.ClearNonAudioTtsTracking();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{SessionId} | Error during PrepareLlmRequestAsync for Turn {TurnId}.",
                SessionId,
                _pipeline.CurrentTurnId ?? Guid.Empty
            );
            await FireErrorAsync(ex);
        }
    }

    private async Task CancelCurrentTurnProcessingAsync()
    {
        await _pipeline.CancelCurrentAsync();
        _context.AbortTurn();
        _metricsTracker.Reset();
    }

    private void HandleInterruption()
    {
        var interruptedTurnId = _pipeline.CurrentTurnId;

        if (!interruptedTurnId.HasValue)
        {
            _logger.LogWarning(
                "{SessionId} | Interruption handled but no active turn ID found.",
                SessionId
            );

            return;
        }

        _metrics.IncrementTurnsInterrupted(SessionId, interruptedTurnId.Value);

        CommitChanges(false);
    }

    private async Task HandleInterruptionCancelAndRefireAsync(
        IInputEvent inputEvent,
        StateMachine<ConversationState, ConversationTrigger>.Transition transition
    )
    {
        HandleInterruption();

        if (transition.Source is ConversationState.Speaking)
        {
            _context.CompleteTurnPart(ASSISTANT_PARTICIPANT.Id, true);
        }

        switch (transition.Trigger)
        {
            case ConversationTrigger.InputDetected:
                await _stateMachine.FireAsync(_inputDetectedTrigger, inputEvent);

                return;
            case ConversationTrigger.InputFinalized:
                await _stateMachine.FireAsync(_inputFinalizedTrigger, inputEvent);

                return;
            default:
                throw new ArgumentException(
                    $"{transition} failed. The state machine is in an invalid state.",
                    nameof(transition)
                );
        }
    }

    private async Task PauseActivitiesAsync()
    {
        _logger.LogInformation(
            "{SessionId} | Action: PauseActivitiesAsync - Stopping Adapters",
            SessionId
        );

        foreach (var adapter in _inputAdapters)
        {
            try
            {
                await adapter.StopAsync(_sessionCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{SessionId} | Error stopping input adapter {AdapterId} during pause.",
                    SessionId,
                    adapter.AdapterId
                );
            }
        }
    }

    private async Task ResumeActivitiesAsync()
    {
        _logger.LogInformation(
            "{SessionId} | Action: ResumeActivitiesAsync - Starting Adapters",
            SessionId
        );

        foreach (var adapter in _inputAdapters)
        {
            try
            {
                await adapter.StartAsync(_sessionCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{SessionId} | Error starting input adapter {AdapterId} during resume.",
                    SessionId,
                    adapter.AdapterId
                );
            }
        }
    }

    private async Task HandleErrorAsync(Exception error)
    {
        _logger.LogError(
            error,
            "{SessionId} | Action: HandleErrorAsync - An error occurred in the state machine.",
            SessionId
        );
        _metrics.IncrementErrors(SessionId, _pipeline.CurrentTurnId, error);
        await _stateMachine.FireAsync(ConversationTrigger.StopRequested);
    }

    private async Task CleanupSessionAsync()
    {
        await CancelCurrentTurnProcessingAsync();

        var inputCleanupTasks = _inputAdapters
            .Select(async adapter =>
            {
                try
                {
                    await adapter.StopAsync(CancellationToken.None);
                    await adapter.DisposeAsync();
                    _logger.LogDebug(
                        "{SessionId} | Input Adapter {AdapterId} stopped and disposed.",
                        SessionId,
                        adapter.AdapterId
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "{SessionId} | Error cleaning up input adapter {AdapterId}.",
                        SessionId,
                        adapter.AdapterId
                    );
                }
            })
            .ToList();

        var outputCleanupTask = Task.Run(async () =>
        {
            try
            {
                await _outputAdapter.StopAsync(CancellationToken.None);
                await _outputAdapter.DisposeAsync();
                _logger.LogDebug(
                    "{SessionId} | Output Adapter {AdapterId} stopped and disposed.",
                    SessionId,
                    _outputAdapter.AdapterId
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{SessionId} | Error cleaning up output adapter {AdapterId}.",
                    SessionId,
                    _outputAdapter.AdapterId
                );
            }
        });

        await Task.WhenAll(inputCleanupTasks.Append(outputCleanupTask));
        _inputAdapters.Clear();
        _logger.LogDebug("{SessionId} | Cleanup finished.", SessionId);
    }

    private void HandleLlmStreamRequested()
    {
        try
        {
            _pipeline.StartLlmStage(_context, _outputChannelWriter, SessionId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(
                ex,
                "{SessionId} | HandleLlmStreamRequested called in invalid state.",
                SessionId
            );
            _ = FireErrorAsync(ex, false);
        }
    }

    private void HandleTtsStreamRequest()
    {
        _pipeline.StartTtsStage(_outputChannelWriter);
    }

    private async Task HandleLlmStreamChunkReceived(
        IOutputEvent outputEvent,
        StateMachine<ConversationState, ConversationTrigger>.Transition _
    )
    {
        if (outputEvent is not LlmChunkEvent chunkEvent)
        {
            _logger.LogWarning(
                "{SessionId} | HandleLlmStreamChunkReceived received unexpected event type: {EventType}",
                SessionId,
                outputEvent.GetType().Name
            );
            await _stateMachine.FireAsync(
                ConversationTrigger.ErrorOccurred,
                new ArgumentException(
                    "Invalid event type for LlmStreamChunkReceived trigger.",
                    nameof(outputEvent)
                )
            );

            return;
        }

        if (!_pipeline.HasValidTurn)
        {
            _logger.LogWarning(
                "{SessionId} | HandleLlmStreamChunkReceived called without a valid TurnId.",
                SessionId
            );

            return;
        }

        if (!_pipeline.IsAudioOutput)
        {
            var latency = _metricsTracker.RecordFirstLlmToken();
            if (latency.HasValue)
            {
                _logger.LogDebug(
                    "{SessionId} | {TurnId} | ⏱️ | First LLM [{LatencyMs:N0} ms]",
                    SessionId,
                    _pipeline.CurrentTurnId!.Value,
                    latency.Value
                );
            }
        }

        _context.AppendToTurn(ASSISTANT_PARTICIPANT.Id, chunkEvent.Chunk);

        await _pipeline.WriteLlmChunkAsync(chunkEvent, _pipeline.TurnCancellationToken!.Value);
    }

    private async Task HandleTtsStreamChunkReceived(
        IOutputEvent outputEvent,
        StateMachine<ConversationState, ConversationTrigger>.Transition _
    )
    {
        if (outputEvent is not TtsChunkEvent chunkEvent)
        {
            _logger.LogWarning(
                "{SessionId} | HandleTtsStreamChunkReceived received unexpected event type: {EventType}",
                SessionId,
                outputEvent.GetType().Name
            );
            await _stateMachine.FireAsync(
                ConversationTrigger.ErrorOccurred,
                new ArgumentException(
                    "Invalid event type for HandleTtsStreamChunkReceived trigger.",
                    nameof(outputEvent)
                )
            );

            return;
        }

        if (!_pipeline.HasValidTurn)
        {
            _logger.LogWarning(
                "{SessionId} | HandleTtsStreamChunkReceived called without a valid TurnId.",
                SessionId
            );

            return;
        }

        var latency = _metricsTracker.RecordFirstTtsChunk();
        if (latency.HasValue)
        {
            _logger.LogDebug(
                "{SessionId} | {TurnId} | ⏱️ | First TTS [{LatencyMs:N0} ms]",
                SessionId,
                _pipeline.CurrentTurnId!.Value,
                latency.Value
            );
        }

        await _pipeline.WriteTtsChunkAsync(chunkEvent, _pipeline.TurnCancellationToken!.Value);
    }

    private void HandleLlmStreamEnded()
    {
        _pipeline.CompleteLlmChannel();

        if (!_pipeline.IsAudioOutput)
        {
            _context.CompleteTurnPart(ASSISTANT_PARTICIPANT.Id);
        }
    }

    private void HandleTtsStreamEnded()
    {
        _pipeline.CompleteTtsChannel();
    }

    private async Task FireErrorAsync(Exception ex, bool stopSession = true)
    {
        try
        {
            if (
                _stateMachine.State != ConversationState.Error
                && _stateMachine.State != ConversationState.Ended
            )
            {
                await _stateMachine.FireAsync(ConversationTrigger.ErrorOccurred, ex);
            }

            if (stopSession && _stateMachine.State != ConversationState.Ended)
            {
                await StopAsync();
            }
        }
        catch (Exception fireEx)
        {
            _logger.LogError(
                fireEx,
                "{SessionId} | Failed to fire ErrorOccurred/StopRequested after initial exception.",
                SessionId
            );
            if (!_sessionCts.IsCancellationRequested)
            {
                await _sessionCts.CancelAsync();
            }
        }
    }

    private async Task EnsureStateMachineStoppedAsync()
    {
        if (
            _stateMachine.State != ConversationState.Ended
            && _stateMachine.State != ConversationState.Error
        )
        {
            _logger.LogWarning(
                "{SessionId} | Session loop ended unexpectedly in state {State}. Forcing StopRequested.",
                SessionId,
                _stateMachine.State
            );
            try
            {
                if (!_sessionCts.IsCancellationRequested)
                {
                    await _sessionCts.CancelAsync();
                }

                await _stateMachine.FireAsync(ConversationTrigger.StopRequested);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{SessionId} | Exception while trying to force StopRequested.",
                    SessionId
                );
            }
        }
    }

    private void CommitSpokenText()
    {
        _context.CompleteTurnPart(ASSISTANT_PARTICIPANT.Id);
    }

    private void HandleIdle()
    {
        var completedTurnId = _pipeline.CurrentTurnId;
        var summary = _metricsTracker.CompleteTurn();

        if (completedTurnId.HasValue && summary.TurnDurationMs.HasValue)
        {
            var sttTime = summary.SttLatencyMs ?? 0;
            var llmTime = summary.FirstLlmTokenMs ?? 0;
            var ttsTime = summary.FirstTtsChunkMs ?? 0;
            var audioTime = summary.FirstAudioMs ?? 0;

            _logger.LogInformation(
                "{SessionId} | {TurnId} | ⏱️ | STT @{SttMs:N0}ms ⇢ LLM @{LlmMs:N0}ms ⇢ TTS @{TtsMs:N0}ms ⇢ AUDIO @{AudioMs:N0}ms | TOTAL @{TotalMs:N0}ms",
                SessionId,
                completedTurnId.Value,
                sttTime,
                llmTime,
                ttsTime,
                audioTime,
                sttTime + llmTime + ttsTime + audioTime
            );
        }

        CommitChanges(false);

        _pipeline.TryCompleteAllChannels();
    }

    private void CommitChanges(bool interrupted)
    {
        foreach (var inputAdapter in _inputAdapters)
        {
            _context.CompleteTurnPart(inputAdapter.Participant.Id, interrupted);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/ConversationSession.StateActions.cs
git commit -m "refactor: extract state actions to partial class, delegate to collaborators"
```

---

### Task 5: Rewrite `ConversationSession.cs` core — fields, constructor, lifecycle only

Remove all code that moved to other partials and collaborators. Update constructor to create `TurnPipelineCoordinator` and declare `TurnMetricsTracker`. Update `DisposeAsync` to dispose the pipeline.

**Files:**
- Modify: `src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/ConversationSession.cs`

- [ ] **Step 1: Replace entire `ConversationSession.cs` with slim core**

```csharp
using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using OpenAI.Chat;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;
using PersonaEngine.Lib.Core.Conversation.Implementations.Context;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Metrics;
using PersonaEngine.Lib.LLM;
using PersonaEngine.Lib.TTS.Synthesis;

using Stateless;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public partial class ConversationSession : IConversationSession
{
    private static readonly ParticipantInfo ASSISTANT_PARTICIPANT = new(
        "ARIA_ASSISTANT_BOT",
        "Aria",
        ChatMessageRole.Assistant
    );

    // Dependencies & Configuration
    private readonly IBargeInStrategy _bargeInStrategy;
    private readonly ConversationContext _context;
    private readonly List<IInputAdapter> _inputAdapters = new();
    private readonly ILogger _logger;
    private readonly ConversationMetrics _metrics;
    private readonly ConversationOptions _options;
    private readonly IOutputAdapter _outputAdapter;

    // Collaborators
    private readonly TurnPipelineCoordinator _pipeline;
    private TurnMetricsTracker _metricsTracker;

    // Communication Channels (Session-level)
    private readonly Channel<IInputEvent> _inputChannel;
    private readonly ChannelWriter<IInputEvent> _inputChannelWriter;
    private readonly Channel<IOutputEvent> _outputChannel;
    private readonly ChannelWriter<IOutputEvent> _outputChannelWriter;

    // Session State
    private readonly CancellationTokenSource _sessionCts = new();
    private bool _isDisposed;
    private Task? _sessionLoopTask;

    public ConversationSession(
        ILogger logger,
        IChatEngine chatEngine,
        ITtsEngine ttsEngine,
        IEnumerable<IInputAdapter> inputAdapters,
        IOutputAdapter outputAdapter,
        ConversationMetrics metrics,
        Guid sessionId,
        ConversationOptions options,
        ConversationContext context,
        IBargeInStrategy bargeInStrategy
    )
    {
        _logger = logger;
        _inputAdapters.AddRange(inputAdapters);
        _outputAdapter = outputAdapter;
        _metrics = metrics;
        SessionId = sessionId;
        _options = options;
        _context = context;
        _bargeInStrategy = bargeInStrategy;

        _pipeline = new TurnPipelineCoordinator(chatEngine, ttsEngine, outputAdapter, logger);

        _inputChannel = Channel.CreateUnbounded<IInputEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
        );
        _inputChannelWriter = _inputChannel.Writer;

        _outputChannel = Channel.CreateUnbounded<IOutputEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
        );
        _outputChannelWriter = _outputChannel.Writer;

        _stateMachine = new StateMachine<ConversationState, ConversationTrigger>(
            ConversationState.Initial
        );
        ConfigureStateMachine();
    }

    public IConversationContext Context => _context;

    public Guid SessionId { get; }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _logger.LogDebug("{SessionId} | Disposing session.", SessionId);

        if (!_sessionCts.IsCancellationRequested)
        {
            await _sessionCts.CancelAsync();
        }

        if (_sessionLoopTask is { IsCompleted: false })
        {
            try
            {
                await _sessionLoopTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning(
                    "{SessionId} | Timeout waiting for session loop task during dispose.",
                    SessionId
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "{SessionId} | Error waiting for session loop task during dispose.",
                    SessionId
                );
            }
        }

        await CleanupSessionAsync();
        await _pipeline.DisposeAsync();

        _sessionCts.Dispose();

        _inputChannelWriter.TryComplete();
        _outputChannelWriter.TryComplete();

        _isDisposed = true;

        GC.SuppressFinalize(this);
        _logger.LogInformation("{SessionId} | Session disposed.", SessionId);
    }

    public async ValueTask RunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_sessionLoopTask is { IsCompleted: false })
        {
            _logger.LogWarning(
                "{SessionId} | RunAsync called while session is already running.",
                SessionId
            );
            await _sessionLoopTask;

            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            _sessionCts.Token,
            cancellationToken
        );
        var linkedToken = linkedCts.Token;

        var inputLoopTask = ProcessInputEventsAsync(linkedToken);
        var outputLoopTask = ProcessOutputEventsAsync(linkedToken);

        _sessionLoopTask = Task.WhenAll(inputLoopTask, outputLoopTask);

        try
        {
            await _stateMachine.FireAsync(ConversationTrigger.InitializeRequested);
            await _sessionLoopTask;

            _logger.LogInformation(
                "{SessionId} | Session run completed. Final State: {State}",
                SessionId,
                _stateMachine.State
            );
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "{SessionId} | RunAsync cancelled externally or by StopAsync.",
                SessionId
            );

            await EnsureStateMachineStoppedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "{SessionId} | Unhandled exception during RunAsync setup or state machine trigger.",
                SessionId
            );
            await FireErrorAsync(ex);
        }
        finally
        {
            _logger.LogDebug("{SessionId} | RunAsync finished execution.", SessionId);
            await CleanupSessionAsync();

            _inputChannelWriter.TryComplete();
            _outputChannelWriter.TryComplete();
            _sessionLoopTask = null;
        }
    }

    public async ValueTask StopAsync()
    {
        _logger.LogDebug(
            "{SessionId} | StopAsync called. Current State: {State}",
            SessionId,
            _stateMachine.State
        );

        if (!_sessionCts.IsCancellationRequested)
        {
            await _sessionCts.CancelAsync();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/ConversationSession.cs
git commit -m "refactor: slim ConversationSession core to fields, ctor, lifecycle only"
```

---

### Task 6: Update `ConfigureStateMachine.cs` — replace `_currentTurnId` with `_pipeline.CurrentTurnId`

**Files:**
- Modify: `src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/ConfigureStateMachine.cs`

- [ ] **Step 1: Update `LogState` to use `_pipeline.CurrentTurnId`**

In `ConfigureStateMachine.cs`, find line 191:
```csharp
        _logger.LogInformation("{SessionId} | {TurnId} | {Emoji} | {DestinationState}",
                               SessionId, _currentTurnId ?? Guid.Empty, emoji, transition.Destination);
```
Replace with:
```csharp
        _logger.LogInformation("{SessionId} | {TurnId} | {Emoji} | {DestinationState}",
                               SessionId, _pipeline.CurrentTurnId ?? Guid.Empty, emoji, transition.Destination);
```

- [ ] **Step 2: Update `ShouldAllowBargeIn` to use `_pipeline.CurrentTurnId`**

In `ConfigureStateMachine.cs`, find line 201:
```csharp
                                         _options,
                                         _stateMachine.State,
                                         inputEvent,
                                         SessionId,
                                         _currentTurnId
```
Replace with:
```csharp
                                         _options,
                                         _stateMachine.State,
                                         inputEvent,
                                         SessionId,
                                         _pipeline.CurrentTurnId
```

- [ ] **Step 3: Commit**

```bash
git add src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/ConfigureStateMachine.cs
git commit -m "refactor: update state machine config to use _pipeline.CurrentTurnId"
```

---

### Task 7: Update `ConversationSessionFactory.cs` — no constructor change needed

The factory constructor signature doesn't change — it still passes `chatEngine`, `ttsEngine`, `outputAdapter` to `ConversationSession`. The session constructor now forwards them to `TurnPipelineCoordinator` internally. Verify the factory compiles without changes.

**Files:**
- Verify: `src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/ConversationSessionFactory.cs`

- [ ] **Step 1: Verify factory requires no changes**

Read the factory file. The constructor call on line 29-40 passes the same parameters. Since `ConversationSession`'s constructor signature is unchanged, no factory update is needed.

- [ ] **Step 2: Build the full solution**

Run: `$HOME/.dotnet/dotnet build src/PersonaEngine/PersonaEngine.sln`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: If build errors, fix them**

Common issues to watch for:
- Missing `using` statements in partial class files
- `_currentTurnId` references remaining anywhere (should be `_pipeline.CurrentTurnId`)
- `_chatEngine` / `_ttsEngine` references remaining (moved to pipeline)
- `_currentTurnCts` references remaining (use `_pipeline.TurnCancellationToken`)
- `_currentLlmChannel` / `_currentTtsChannel` references remaining (moved to pipeline)
- `_llmTask` / `_ttsTask` / `_audioTask` references remaining (moved to pipeline)
- Any stopwatch field references remaining (moved to `_metricsTracker`)
- `_llmFinishReason` / `_ttsFinishReason` / `_audioFinishReason` references remaining (moved to `_metricsTracker`)
- Duplicate method definitions across partial files

- [ ] **Step 4: Commit build fix if needed**

```bash
git add -A
git commit -m "fix: resolve build errors from refactor"
```

---

### Task 8: Run CSharpier formatting

**Files:**
- All new and modified files in `src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/`

- [ ] **Step 1: Run CSharpier**

Run: `cd src/PersonaEngine && $HOME/.dotnet/dotnet csharpier .`
Expected: Formatted N files

- [ ] **Step 2: Rebuild to verify formatting didn't break anything**

Run: `$HOME/.dotnet/dotnet build src/PersonaEngine/PersonaEngine.sln`
Expected: Build succeeded

- [ ] **Step 3: Commit formatting**

```bash
git add -A
git commit -m "style: apply CSharpier formatting to refactored files"
```

---

### Task 9: Verify the `SynthesizeStreamingAsync` session ID parameter

During review, the `TurnPipelineCoordinator.StartTtsStage` method passes `turnId.Value` for both the `turnId` and `sessionId` parameters of `SynthesizeStreamingAsync`. This needs to be verified against the original code.

**Files:**
- Modify: `src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/TurnPipelineCoordinator.cs`

- [ ] **Step 1: Check original `HandleTtsStreamRequest` call**

In the original `ConversationSession.cs` line 1120-1126, the call was:
```csharp
_ttsTask = _ttsEngine.SynthesizeStreamingAsync(
    outputChannelReader,
    outputChannelWriter,
    turnId.Value,
    SessionId,           // <-- this is the session ID, not turn ID
    cancellationToken: turnCts.Token
);
```

The coordinator needs the session ID passed in. Update `StartTtsStage` signature:

```csharp
public void StartTtsStage(ChannelWriter<IOutputEvent> outputWriter, Guid sessionId)
```

And the call inside:
```csharp
_ttsTask = _ttsEngine.SynthesizeStreamingAsync(
    _llmChannel,
    outputWriter,
    turnId.Value,
    sessionId,
    cancellationToken: turnCts.Token
);
```

- [ ] **Step 2: Update `HandleTtsStreamRequest` in `ConversationSession.StateActions.cs`**

```csharp
private void HandleTtsStreamRequest()
{
    _pipeline.StartTtsStage(_outputChannelWriter, SessionId);
}
```

- [ ] **Step 3: Similarly, verify and fix any other `Speaking` state re-entry for TTS**

In `ConfigureStateMachine.cs` line 97, Speaking state has:
```csharp
.InternalTransition(ConversationTrigger.TtsStreamStarted, HandleTtsStreamRequest)
```
This calls the same `HandleTtsStreamRequest` method which now passes `SessionId` — correct.

- [ ] **Step 4: Rebuild**

Run: `$HOME/.dotnet/dotnet build src/PersonaEngine/PersonaEngine.sln`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "fix: pass correct sessionId to SynthesizeStreamingAsync in pipeline coordinator"
```

---

### Task 10: Final verification and CSharpier check

- [ ] **Step 1: Run CSharpier check**

Run: `cd src/PersonaEngine && $HOME/.dotnet/dotnet csharpier check .`
Expected: All files formatted correctly

- [ ] **Step 2: Run full build**

Run: `$HOME/.dotnet/dotnet build src/PersonaEngine/PersonaEngine.sln`
Expected: Build succeeded with 0 errors, 0 warnings (or only pre-existing warnings)

- [ ] **Step 3: Review final file sizes**

Run `wc -l` on all session files to verify no single file exceeds ~300 lines:

```bash
wc -l src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/*.cs
```

- [ ] **Step 4: Verify no remaining references to moved fields**

Search for any leftover references to fields that should have been removed from `ConversationSession`:

```bash
grep -rn "_currentTurnId\|_currentTurnCts\|_llmTask\|_ttsTask\|_audioTask\|_currentLlmChannel\|_currentTtsChannel\|_chatEngine\|_ttsEngine\|_llmStopwatch\|_ttsStopwatch\|_audioPlaybackStopwatch\|_turnStopwatch\|_firstLlmTokenLatencyStopwatch\|_firstTtsChunkLatencyStopwatch\|_firstAudioLatencyStopwatch\|_llmFinishReason\|_ttsFinishReason\|_audioFinishReason\|_currentTurnFirstLlmTokenLatencyMs\|_currentTurnFirstTtsChunkLatencyMs\|_currentTurnFirstAudioLatencyMs\|_currentTurnFirstSttChunkLatencyMs\|_sttStartTime" src/PersonaEngine/PersonaEngine.Lib/Core/Conversation/Implementations/Session/ConversationSession*.cs
```

Expected: No matches (these fields should only exist in `TurnMetricsTracker.cs` and `TurnPipelineCoordinator.cs` now)

- [ ] **Step 5: Commit if any final fixes were needed**

```bash
git add -A
git commit -m "refactor: final cleanup of ConversationSession SRP refactor"
```
