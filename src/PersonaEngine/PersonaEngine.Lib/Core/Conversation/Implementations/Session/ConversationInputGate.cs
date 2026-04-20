using Microsoft.Extensions.Logging;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

namespace PersonaEngine.Lib.Core.Conversation.Implementations.Session;

/// <summary>
///     Hold-count implementation of <see cref="IConversationInputGate" />.
///     Lock-free on the read path (single <see cref="Volatile.Read(ref int)" />);
///     <see cref="Interlocked" /> increment/decrement on open/close.
/// </summary>
public sealed class ConversationInputGate : IConversationInputGate
{
    private readonly ILogger<ConversationInputGate> _logger;
    private int _holdCount;

    public ConversationInputGate(ILogger<ConversationInputGate> logger)
    {
        _logger = logger;
    }

    public bool IsOpen => Volatile.Read(ref _holdCount) == 0;

    public IDisposable CloseScope(string reason)
    {
        var newCount = Interlocked.Increment(ref _holdCount);
        if (newCount == 1)
        {
            _logger.LogTrace("Input gate closing. Reason: {Reason}", reason ?? string.Empty);
        }

        return new Scope(this, reason);
    }

    private void ReleaseScope(string reason)
    {
        var newCount = Interlocked.Decrement(ref _holdCount);
        if (newCount < 0)
        {
            // Should be unreachable — Scope guards against double-release via its own flag.
            Interlocked.Exchange(ref _holdCount, 0);
            _logger.LogWarning(
                "Input gate hold count went negative; clamped to zero. Last-released reason: {Reason}",
                reason ?? string.Empty
            );
            return;
        }

        if (newCount == 0)
        {
            _logger.LogTrace(
                "Input gate reopened. Last-released reason: {Reason}",
                reason ?? string.Empty
            );
        }
    }

    /// <summary>
    ///     Per-caller scope. Dispose is idempotent — only the first call releases the hold.
    /// </summary>
    private sealed class Scope : IDisposable
    {
        private readonly ConversationInputGate _owner;
        private readonly string _reason;
        private int _disposed;

        public Scope(ConversationInputGate owner, string reason)
        {
            _owner = owner;
            _reason = reason ?? string.Empty;
        }

        public void Dispose()
        {
            // Ensure double-dispose is a no-op: only the first caller to flip _disposed releases.
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _owner.ReleaseScope(_reason);
        }
    }
}
