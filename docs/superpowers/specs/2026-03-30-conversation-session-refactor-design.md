# ConversationSession SRP Refactor — Design Spec

**Date:** 2026-03-30
**Type:** Pure refactor (no behavioral changes)
**Scope:** `ConversationSession.cs` (~1427 lines) → extract collaborator types for SRP and maintainability
**Constraints:** Performance is top priority. Zero unnecessary allocations. Follow C# standards.

## Problem

`ConversationSession` violates SRP by handling:

1. **Metrics & timing** — 12 stopwatch fields, latency values, completion reasons scattered across ~8 methods
2. **Pipeline lifecycle** — `_llmTask`, `_ttsTask`, `_audioTask`, two channels, CTS management with duplicated create/complete/cancel patterns
3. **State machine coordination** — 13 states, trigger firing, event routing
4. **Turn lifecycle** — cancellation, context commits, adapter coordination
5. **Event loop processing** — two ~80-100 line loops with similar error handling

The class has 30+ fields with no encapsulation boundaries. Duplicated patterns include channel `TryComplete` (4 call sites), task cancellation await (3 identical calls), and stopwatch start/stop/reset sequences.

There is also a bug: `ProcessOutputEventsAsync` completes the **input** channel writer in its `finally` block instead of the output channel writer.

## Approach: Extract Collaborators

Extract two focused types that `ConversationSession` owns and delegates to. Keep event loops as methods on the session (not worth abstracting — see rationale below). Fix the channel completion bug.

## Extracted Type 1: `TurnMetricsTracker` (struct)

**Responsibility:** Owns all per-turn timing state. Provides methods to start/stop/record metrics at each pipeline stage. Returns a summary struct on turn completion.

**Why a struct:** Single owner (`ConversationSession`), never boxed, never passed through interfaces. Lives inline — zero heap allocation.

### API

```csharp
internal struct TurnMetricsTracker
{
    // --- Fields (all moved from ConversationSession) ---
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
    private double? _firstSttChunkLatencyMs;

    public CompletionReason LlmFinishReason;
    public CompletionReason TtsFinishReason;
    public CompletionReason AudioFinishReason;

    // --- Lifecycle ---
    public void StartTurn();                // starts turn + LLM latency stopwatches, resets reasons
    public void RecordSttStart();           // sets _sttStartTime
    public void RecordSttEnd();             // calculates STT latency from _sttStartTime

    // --- Cascading first-chunk latency ---
    public void RecordFirstLlmToken();      // stops LLM latency sw → starts TTS latency sw
    public void RecordFirstTtsChunk();      // stops TTS latency sw → starts audio latency sw
    public void RecordFirstAudioChunk();    // stops audio latency sw

    // --- Per-stage duration ---
    public void StartLlmStopwatch();
    public void StopLlmStopwatch();
    public void StartTtsStopwatch();
    public void StopTtsStopwatch();
    public void StartAudioStopwatch();
    public void StopAudioStopwatch();

    // --- Terminal ---
    public TurnTimingSummary CompleteTurn(); // stops all, returns summary, resets
    public void Reset();                     // nulls all (for cancellation paths)
}

internal readonly record struct TurnTimingSummary(
    double? TurnDurationMs,
    double? SttLatencyMs,
    double? FirstLlmTokenMs,
    double? FirstTtsChunkMs,
    double? FirstAudioMs,
    double? LlmDurationMs,
    double? TtsDurationMs,
    double? AudioDurationMs,
    CompletionReason OverallCompletionReason
);
```

### What moves here

| From method | Logic moved |
|---|---|
| `PrepareLlmRequestAsync` | `StartTurn()` — starts turn stopwatch + first LLM latency stopwatch, resets completion reasons |
| `TrackLlmFirstTokenLatency` | `RecordFirstLlmToken()` — entire method body |
| `HandleTtsStreamChunkReceived` | `RecordFirstTtsChunk()` — stopwatch stop/start/record block |
| `ProcessOutputEventsAsync` | `StartLlmStopwatch()`, `StopLlmStopwatch()`, etc. + completion reason assignments |
| `HandleIdle` | `CompleteTurn()` — timing summary construction + field reset |
| `CancelCurrentTurnProcessingAsync` | `Reset()` — stopwatch nulling + latency field nulling |

### What stays in `ConversationSession`

- `ConversationMetrics` (OpenTelemetry singleton) — the tracker produces values, the session records them to histograms
- Turn timing log formatting — reads `TurnTimingSummary`, writes structured log

## Extracted Type 2: `TurnPipelineCoordinator` (class)

**Responsibility:** Owns the LLM → TTS → Audio pipeline task lifecycle and inter-stage channels. Handles turn-scoped cancellation, channel creation/completion, and task await/cleanup.

**Why a class:** Holds `Task` references, `Channel<T>`, and `CancellationTokenSource` — reference semantics required. Implements `IAsyncDisposable`. Created once per session, reused across turns (no per-turn allocation of the coordinator itself).

### API

```csharp
internal class TurnPipelineCoordinator : IAsyncDisposable
{
    // Constructor dependencies (moved from ConversationSession)
    private readonly IChatEngine _chatEngine;
    private readonly ITtsEngine _ttsEngine;
    private readonly IOutputAdapter _outputAdapter;
    private readonly ILogger _logger;

    // Turn-scoped state
    private CancellationTokenSource? _turnCts;
    private Guid? _currentTurnId;

    // Pipeline tasks
    private Task? _llmTask;
    private Task? _ttsTask;
    private Task? _audioTask;

    // Inter-stage channels
    private Channel<LlmChunkEvent>? _llmChannel;
    private Channel<TtsChunkEvent>? _ttsChannel;

    // --- Properties ---
    public Guid? CurrentTurnId { get; }
    public CancellationToken? TurnCancellationToken { get; }

    // --- Turn lifecycle ---
    public Guid StartNewTurn(CancellationToken sessionToken);
    // Calls CancelCurrentAsync() if turn active, creates linked CTS, sets turn ID

    public ValueTask CancelCurrentAsync();
    // Cancels CTS → awaits all 3 tasks (swallows OCE) → completes channels with OCE → disposes CTS → nulls all

    // --- Pipeline stage startup ---
    public void StartLlmStage(IConversationContext context, ChannelWriter<IOutputEvent> outputWriter, Guid sessionId);
    // Creates _llmChannel, starts _llmTask

    public void StartTtsStage(ChannelWriter<IOutputEvent> outputWriter);
    // Creates _ttsChannel, starts _ttsTask, conditionally starts _audioTask (IAudioOutputAdapter)

    public void StartAudioStage(ChannelWriter<IOutputEvent> outputWriter);
    // For deferred audio start (Speaking state re-entry)

    // --- Channel forwarding ---
    public bool TryWriteLlmChunk(LlmChunkEvent chunk);
    public bool TryWriteTtsChunk(TtsChunkEvent chunk);
    public void CompleteLlmChannel();
    public void CompleteTtsChannel();
    public void TryCompleteAllChannels();

    // --- Disposal ---
    public ValueTask DisposeAsync();
}
```

### What moves here

| From method | Logic moved |
|---|---|
| `CancelCurrentTurnProcessingAsync` | Pipeline teardown: CTS cancel, task awaits, channel completion, CTS dispose (context abort stays in session) |
| `WaitForTaskCancellationAsync` | Entire method — becomes private helper inside coordinator |
| `HandleLlmStreamRequested` | Channel creation + `_llmTask` start |
| `HandleTtsStreamRequest` | Channel creation + `_ttsTask`/`_audioTask` start |
| `HandleLlmStreamChunkReceived` | Channel write (validation + `TryWrite`) |
| `HandleTtsStreamChunkReceived` | Channel write (validation + `TryWrite`) |
| `HandleLlmStreamEnded` | `CompleteLlmChannel()` |
| `HandleTtsStreamEnded` | `CompleteTtsChannel()` |
| `HandleIdle` | `TryCompleteAllChannels()` |
| `PrepareLlmRequestAsync` | CTS creation, turn ID generation → `StartNewTurn()` |

### What stays in `ConversationSession`

- State machine trigger firing
- `IConversationContext` mutations (add turn, append text, commit, abort)
- Adapter lifecycle (initialize, start, stop, dispose) — these are session-level, not turn-level

## Event Loops: Keep In Place (Not Extracted)

**Decision:** `ProcessInputEventsAsync` and `ProcessOutputEventsAsync` remain as methods on `ConversationSession`.

**Rationale:**

1. **Not truly generic.** Input loop tracks STT timing and maps `IInputEvent` → trigger. Output loop tracks LLM/TTS/Audio metrics and maps `IOutputEvent` → trigger. An abstraction covering both requires generics + callbacks — the shared skeleton is only ~15 lines.
2. **Performance.** A `Func<TEvent, (Trigger, object?)>` delegate on every chunk adds allocation + virtual dispatch on the hot path.
3. **Already addressed.** Once metrics move to `TurnMetricsTracker` and channels to `TurnPipelineCoordinator`, each loop shrinks to ~30-40 lines of event-type-specific logic. The remaining similarity isn't worth abstracting.

**Organization:** Move both loops to a `ConversationSession.EventProcessing.cs` partial class file alongside the static `MapInputEventToTrigger` / `MapOutputEventToTrigger` methods.

## Bug Fix

`ProcessOutputEventsAsync` currently completes `_inputChannelWriter` in its `finally` block. This should complete `_outputChannelWriter`. Fixed as part of the refactor — no behavioral impact since the session is shutting down when this executes, but it's incorrect.

## File Layout After Refactor

```
Core/Conversation/Implementations/Session/
  ConversationSession.cs                        # Fields, ctor, lifecycle (Run/Stop/Dispose)    ~150 lines
  ConversationSession.ConfigureStateMachine.cs  # State machine config (unchanged)              ~200 lines
  ConversationSession.EventProcessing.cs        # Input/output loops + event mapping            ~120 lines
  ConversationSession.StateActions.cs           # Handle*/Prepare*/Cleanup* (thin delegations)  ~250 lines
  TurnMetricsTracker.cs                         # struct — timing/latency/completion             ~180 lines
  TurnPipelineCoordinator.cs                    # class — pipeline task/channel lifecycle         ~200 lines
```

**Total: ~1100 lines across 6 files (down from ~1427 in 2 files). Max file size: ~250 lines.**

## Field Migration Summary

### Removed from `ConversationSession` (24 fields)

Moved to `TurnMetricsTracker` (15): `_turnStopwatch`, `_llmStopwatch`, `_ttsStopwatch`, `_audioPlaybackStopwatch`, `_firstLlmTokenLatencyStopwatch`, `_firstTtsChunkLatencyStopwatch`, `_firstAudioLatencyStopwatch`, `_currentTurnFirstLlmTokenLatencyMs`, `_currentTurnFirstTtsChunkLatencyMs`, `_currentTurnFirstAudioLatencyMs`, `_sttStartTime`, `_currentTurnFirstSttChunkLatencyMs`, `_llmFinishReason`, `_ttsFinishReason`, `_audioFinishReason`

Moved to `TurnPipelineCoordinator` (9): `_chatEngine`, `_ttsEngine`, `_currentTurnCts`, `_currentTurnId`, `_llmTask`, `_ttsTask`, `_audioTask`, `_currentLlmChannel`, `_currentTtsChannel`

### Added to `ConversationSession` (2 fields)

`_pipeline` (`TurnPipelineCoordinator`), `_metricsTracker` (`TurnMetricsTracker`)

### Remaining in `ConversationSession` (13 fields)

`_logger`, `_bargeInStrategy`, `_options`, `_metrics`, `_context`, `_inputAdapters`, `_outputAdapter`, `_inputChannel`, `_inputChannelWriter`, `_outputChannel`, `_outputChannelWriter`, `_sessionCts`, `_isDisposed`, `_sessionLoopTask`

Note: `_outputAdapter` is kept in both `ConversationSession` (for adapter lifecycle management) and passed to `TurnPipelineCoordinator` (for `SendAsync` calls). This avoids the coordinator needing to know about adapter init/start/stop/dispose.

## Performance Guarantees

- **Zero new heap allocations per turn:** `TurnMetricsTracker` is a struct (inline). `TurnPipelineCoordinator` is created once per session. Per-turn allocations (channels, CTS) are the same as today.
- **No virtual dispatch added to hot path:** No new interfaces, no delegates on chunk processing. Direct method calls only.
- **No boxing:** `TurnMetricsTracker` is never passed through interfaces or stored as `object`.
- **`TurnTimingSummary` is `readonly record struct`:** Stack-allocated, passed by value. Used once per turn completion.
