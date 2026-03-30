using System.Collections.Immutable;

namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Implements editor state management
/// </summary>
public class EditorStateManager : IEditorStateManager
{
    private readonly Dictionary<string, ActiveOperation> _activeOperations = new();

    private readonly Dictionary<string, object> _stateValues = new();

    public event EventHandler<EditorStateChangedEventArgs> StateChanged;

    public bool HasUnsavedChanges { get; private set; } = false;

    public void MarkAsChanged(string? sectionKey = null)
    {
        SetStateValue("HasUnsavedChanges", false, true);
        HasUnsavedChanges = true;
    }

    public void MarkAsSaved()
    {
        SetStateValue("HasUnsavedChanges", true, false);
        HasUnsavedChanges = false;
    }

    public ActiveOperation? GetActiveOperation()
    {
        return _activeOperations.Values.FirstOrDefault();
    }

    public void RegisterActiveOperation(ActiveOperation operation)
    {
        _activeOperations[operation.Id] = operation;
        FireStateChanged("ActiveOperations", null, _activeOperations.Values.ToImmutableList());
    }

    public void ClearActiveOperation(string operationId)
    {
        if (_activeOperations.TryGetValue(operationId, out var operation))
        {
            _activeOperations.Remove(operationId);
            operation.CancellationSource.Dispose();
            FireStateChanged("ActiveOperations", null, _activeOperations.Values.ToImmutableList());
        }
    }

    private void SetStateValue<T>(string key, T? oldValue, T newValue)
        where T : struct
    {
        _stateValues[key] = newValue;
        FireStateChanged(key, oldValue, newValue);
    }

    private void FireStateChanged<T>(string key, T? oldValue, T newValue)
        where T : struct
    {
        StateChanged?.Invoke(this, new EditorStateChangedEventArgs(key, oldValue, newValue));
    }

    private void FireStateChanged<T>(string key, T? oldValue, T newValue)
    {
        StateChanged?.Invoke(this, new EditorStateChangedEventArgs(key, oldValue, newValue));
    }
}
