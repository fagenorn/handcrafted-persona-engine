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
    private readonly IConversationInputGate _inputGate;
    private readonly List<IInputAdapter> _inputAdapters = new();
    private readonly ILogger _logger;
    private readonly ConversationMetrics _metrics;
    private readonly ConversationOptions _options;
    private readonly IOutputAdapter _outputAdapter;

    // Collaborators
    private readonly TurnPipelineCoordinator _pipeline;
    private TurnMetricsTracker _metricsTracker = new();

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
        IBargeInStrategy bargeInStrategy,
        IConversationInputGate inputGate
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
        _inputGate = inputGate;

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

    public ConversationState CurrentState => _stateMachine.State;

    public bool IsPaused => _stateMachine.State == ConversationState.Paused;

    public Guid SessionId { get; }

    /// <inheritdoc />
    public event Action<ConversationState>? StateChanged;

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

    public ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            return ValueTask.CompletedTask;

        if (IsPaused)
            return ValueTask.CompletedTask;

        _logger.LogInformation("{SessionId} | Pause requested.", SessionId);
        return new ValueTask(_stateMachine.FireAsync(ConversationTrigger.PauseRequested));
    }

    public ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            return ValueTask.CompletedTask;

        if (!IsPaused)
            return ValueTask.CompletedTask;

        _logger.LogInformation("{SessionId} | Resume requested.", SessionId);
        return new ValueTask(_stateMachine.FireAsync(ConversationTrigger.ResumeRequested));
    }

    /// <summary>
    ///     Fires <see cref="ConversationTrigger.CancelRequested" /> on the FSM. The state
    ///     machine is the single source of truth: only <see cref="ConversationState.ActiveTurn" />
    ///     transitions to <see cref="ConversationState.Cancelled" />; every other state
    ///     ignores the trigger as a benign no-op. No pre-state check here — any check would
    ///     race with the FSM.
    /// </summary>
    public ValueTask CancelAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            return ValueTask.CompletedTask;

        _logger.LogInformation("{SessionId} | Cancel requested.", SessionId);
        return new ValueTask(_stateMachine.FireAsync(ConversationTrigger.CancelRequested));
    }

    /// <summary>
    ///     Fires <see cref="ConversationTrigger.RetryRequested" /> on the FSM. Only
    ///     <see cref="ConversationState.Error" /> transitions to
    ///     <see cref="ConversationState.Idle" />; every other state ignores the trigger as a
    ///     benign no-op. No pre-state check here — any check would race with the FSM.
    /// </summary>
    public ValueTask RetryAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            return ValueTask.CompletedTask;

        _logger.LogInformation("{SessionId} | Retry requested.", SessionId);
        return new ValueTask(_stateMachine.FireAsync(ConversationTrigger.RetryRequested));
    }
}
