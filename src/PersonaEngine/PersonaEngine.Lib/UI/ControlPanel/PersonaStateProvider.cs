using PersonaEngine.Lib.Core.Conversation.Abstractions.Adapters;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

namespace PersonaEngine.Lib.UI.ControlPanel;

/// <summary>
/// Bridges conversation state and audio amplitude into a simple API for UI effects.
/// Call <see cref="Update"/> once per frame from the render loop.
/// </summary>
public sealed class PersonaStateProvider
{
    private readonly IConversationOrchestrator _orchestrator;
    private readonly IAudioAmplitudeProvider _amplitudeProvider;

    private PersonaUiState _state;
    private PersonaUiState _previousState;
    private float _stateElapsed;
    private bool _stateJustChanged;

    public PersonaStateProvider(
        IConversationOrchestrator orchestrator,
        IAudioAmplitudeProvider amplitudeProvider
    )
    {
        _orchestrator = orchestrator;
        _amplitudeProvider = amplitudeProvider;
    }

    public PersonaUiState State => _state;

    public PersonaUiState PreviousState => _previousState;

    public bool StateJustChanged => _stateJustChanged;

    public float StateElapsed => _stateElapsed;

    public float AudioAmplitude => _amplitudeProvider.CurrentAmplitude;

    public bool IsAudioPlaying => _amplitudeProvider.IsPlaying;

    public void Update(float dt)
    {
        _stateJustChanged = false;

        var newState = DeriveState();

        if (newState != _state)
        {
            _previousState = _state;
            _state = newState;
            _stateElapsed = 0f;
            _stateJustChanged = true;
        }

        _stateElapsed += dt;
    }

    private PersonaUiState DeriveState()
    {
        var sessionIds = _orchestrator.GetActiveSessionIds();
        Guid? firstId = null;

        foreach (var id in sessionIds)
        {
            firstId = id;
            break;
        }

        if (firstId is null)
            return PersonaUiState.NoSession;

        IConversationSession session;
        try
        {
            session = _orchestrator.GetSession(firstId.Value);
        }
        catch (KeyNotFoundException)
        {
            return PersonaUiState.NoSession;
        }

        var conversationState = session.CurrentState;

        if (
            conversationState
            is ConversationState.Ended
                or ConversationState.Error
                or ConversationState.Initial
        )
        {
            return PersonaUiState.NoSession;
        }

        var pendingTurn = session.Context.PendingTurn;

        if (pendingTurn is not null)
        {
            return pendingTurn.Messages.Count > 0
                ? PersonaUiState.Speaking
                : PersonaUiState.Thinking;
        }

        return PersonaUiState.Idle;
    }
}
