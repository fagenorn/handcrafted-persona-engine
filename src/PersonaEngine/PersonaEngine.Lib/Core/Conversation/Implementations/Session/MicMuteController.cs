using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

public sealed class MicMuteController(IConversationInputGate gate) : IMicMuteController
{
    private const string ScopeReason = "User mic mute";
    private readonly object _lock = new();
    private IDisposable? _scope;

    public bool IsMuted => Volatile.Read(ref _scope) is not null;

    public event Action<bool>? MutedChanged;

    public void SetMuted(bool muted)
    {
        bool changed;
        lock (_lock)
        {
            if (muted && _scope is null)
            {
                _scope = gate.CloseScope(ScopeReason);
                changed = true;
            }
            else if (!muted && _scope is not null)
            {
                _scope.Dispose();
                _scope = null;
                changed = true;
            }
            else
            {
                changed = false;
            }
        }

        if (changed)
        {
            MutedChanged?.Invoke(muted);
        }
    }
}
