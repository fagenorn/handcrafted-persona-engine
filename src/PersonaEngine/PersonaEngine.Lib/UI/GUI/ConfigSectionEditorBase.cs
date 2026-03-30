namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Base implementation for configuration section editors
/// </summary>
public abstract class ConfigSectionEditorBase : IConfigSectionEditor, IDisposable
{
    protected readonly IUiConfigurationManager ConfigManager;

    protected readonly IEditorStateManager StateManager;

    protected internal bool _hasUnsavedChanges = false;

    protected ConfigSectionEditorBase(
        IUiConfigurationManager configManager,
        IEditorStateManager stateManager
    )
    {
        ConfigManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        StateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    }

    public abstract string SectionKey { get; }

    public abstract string DisplayName { get; }

    public bool HasUnsavedChanges => _hasUnsavedChanges;

    public virtual void Initialize() { }

    public abstract void Render();

    public virtual void RenderMenuItems() { }

    public virtual void Update(float deltaTime) { }

    public virtual void OnConfigurationChanged(ConfigurationChangedEventArgs args)
    {
        if (
            args.Type
            is ConfigurationChangedEventArgs.ChangeType.Saved
                or ConfigurationChangedEventArgs.ChangeType.Reloaded
        )
        {
            _hasUnsavedChanges = false;
        }
    }

    public virtual void Dispose()
    {
        // Base implementation does nothing
        GC.SuppressFinalize(this);
    }

    protected void MarkAsChanged()
    {
        if (!_hasUnsavedChanges)
        {
            _hasUnsavedChanges = true;
            StateManager.MarkAsChanged(SectionKey);
        }
    }

    protected void MarkAsSaved()
    {
        _hasUnsavedChanges = false;
        StateManager.MarkAsSaved();
    }
}
