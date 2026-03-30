namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Manages the state of the configuration editor UI
/// </summary>
public interface IEditorStateManager
{
    bool HasUnsavedChanges { get; }

    event EventHandler<EditorStateChangedEventArgs> StateChanged;

    void MarkAsChanged(string? sectionKey = null);

    void MarkAsSaved();

    ActiveOperation? GetActiveOperation();

    void RegisterActiveOperation(ActiveOperation operation);

    void ClearActiveOperation(string operationId);
}
