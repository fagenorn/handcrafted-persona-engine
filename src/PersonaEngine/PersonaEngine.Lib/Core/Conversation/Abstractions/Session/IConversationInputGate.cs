namespace PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

/// <summary>
///     Gate controlling whether conversation input events are processed by active sessions.
///     Composable: multiple holders each obtain their own <see cref="IDisposable" /> scope
///     via <see cref="CloseScope" />; the gate stays closed while any scope is undisposed.
///     Used by UI surfaces that need to suppress the avatar's response while the operator
///     is calibrating the microphone or tuning speech detection.
/// </summary>
public interface IConversationInputGate
{
    /// <summary>
    ///     <see langword="true" /> when input events should flow to sessions. <see langword="false" />
    ///     while at least one caller holds the gate closed. Implementations guarantee an atomic
    ///     read suitable for the session's input hot-path.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    ///     Closes the gate for the returned scope's lifetime. Dispose the scope to release
    ///     this caller's hold; the gate only reopens when every scope is disposed.
    /// </summary>
    /// <param name="reason">Diagnostic label logged at trace level on transitions.</param>
    /// <returns>A disposable scope. Double-dispose is a safe no-op.</returns>
    IDisposable CloseScope(string reason);
}
