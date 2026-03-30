namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Event arguments for editor state changes
/// </summary>
public class EditorStateChangedEventArgs : EventArgs
{
    public EditorStateChangedEventArgs(string stateKey, object oldValue, object newValue)
    {
        StateKey = stateKey;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public string StateKey { get; }

    public object OldValue { get; }

    public object NewValue { get; }
}
