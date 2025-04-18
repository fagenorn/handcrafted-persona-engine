using System.Numerics;

using Hexa.NET.ImGui;
using Hexa.NET.Utilities.Text;

using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     The main configuration editor component with improved architecture and performance
/// </summary>
public class ConfigEditorComponent : IRenderComponent
{
    private readonly IUiConfigurationManager _configManager;

    private readonly INotificationService _notificationService;

    private readonly IConfigSectionRegistry _sectionRegistry;

    private readonly IEditorStateManager _stateManager;

    private readonly IUiThemeManager _themeManager;

    private bool _isInitialized = false;

    public ConfigEditorComponent(
        IUiConfigurationManager configManager,
        IEditorStateManager     stateManager,
        IConfigSectionRegistry  sectionRegistry,
        IUiThemeManager         themeManager,
        INotificationService    notificationService)
    {
        _configManager       = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _stateManager        = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _sectionRegistry     = sectionRegistry ?? throw new ArgumentNullException(nameof(sectionRegistry));
        _themeManager        = themeManager ?? throw new ArgumentNullException(nameof(themeManager));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

        // Subscribe to configuration change events
        _configManager.ConfigurationChanged += OnConfigurationChanged;

        // Subscribe to state change events
        _stateManager.StateChanged += OnStateChanged;
    }

    public void Dispose()
    {
        // Unsubscribe from events
        _configManager.ConfigurationChanged -= OnConfigurationChanged;
        _stateManager.StateChanged          -= OnStateChanged;

        // Dispose all registered section editors
        foreach ( var section in _sectionRegistry.GetSections() )
        {
            (section as IDisposable)?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    #region IRenderComponent Implementation

    public bool UseSpout => false;

    public string SpoutTarget => string.Empty;

    public int Priority => 0;

    public void Initialize(GL gl, IView view, IInputContext input)
    {
        if ( _isInitialized )
        {
            return;
        }

        // Apply global UI theme
        _themeManager.ApplyTheme();

        // Initialize all registered section editors
        foreach ( var section in _sectionRegistry.GetSections() )
        {
            section.Initialize();
        }

        _isInitialized = true;
    }

    public void Render(float deltaTime)
    {
        // Set window parameters
        // ImGui.SetNextWindowSize(new Vector2(900, 650), ImGuiCond.FirstUseEver);

        // RenderMenuBar();

        // Show notification banner if there are unsaved changes
        // if ( _stateManager.HasUnsavedChanges )
        // {
        //     RenderUnsavedChangesBanner();
        // }

        // Show notifications if present
        // RenderNotifications();

        // Render tab bar for sections
        RenderSectionTabs();
    }

    public void Update(float deltaTime)
    {
        // Update all registered section editors
        foreach ( var section in _sectionRegistry.GetSections() )
        {
            section.Update(deltaTime);
        }

        // Update notification service
        _notificationService.Update(deltaTime);
    }

    public void Resize()
    {
        // Not needed for this component
    }

    #endregion

    #region Private UI Methods

    private void RenderMenuBar()
    {
        if ( ImGui.BeginMenuBar() )
        {
            // Render section-specific menu items
            foreach ( var section in _sectionRegistry.GetSections() )
            {
                section.RenderMenuItems();
            }

            // Render active operations status on the right side
            RenderStatusIndicators();

            ImGui.EndMenuBar();
        }
    }

    private void RenderStatusIndicators()
    {
        var activeOperation = _stateManager.GetActiveOperation();
        if ( activeOperation != null )
        {
            var menuWidth = ImGui.GetWindowWidth();
            ImGui.SameLine(menuWidth - 160);

            // Display operation name
            ImGui.AlignTextToFramePadding();
            ImGui.Text(activeOperation.Name);

            // Animated dots
            var time = (int)(ImGui.GetTime() * 2) % 4;
            for ( var i = 0; i < time; i++ )
            {
                ImGui.SameLine(0, 2);
                ImGui.Text(".");
            }

            // Progress bar
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.ProgressBar(activeOperation.Progress, new Vector2(100, 15), "");
        }
    }

    private void RenderUnsavedChangesBanner()
    {
        UiStyler.WithStyleColor(ImGuiCol.ChildBg, new Vector4(0.92f, 0.73f, 0.0f, 0.2f), () =>
                                                                                         {
                                                                                             UiStyler.WithStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 5), () =>
                                                                                                                                                                   {
                                                                                                                                                                       ImGui.BeginChild("UnsavedChangesBar", new Vector2(ImGui.GetContentRegionAvail().X, 40), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);

                                                                                                                                                                       ImGui.AlignTextToFramePadding();
                                                                                                                                                                       ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "You have unsaved changes!");

                                                                                                                                                                       ImGui.SameLine(ImGui.GetContentRegionAvail().X - 200);

                                                                                                                                                                       if ( ImGui.Button("Save Changes", new Vector2(100, 30)) )
                                                                                                                                                                       {
                                                                                                                                                                           SaveConfiguration();
                                                                                                                                                                       }

                                                                                                                                                                       ImGui.SameLine();

                                                                                                                                                                       if ( ImGui.Button("Discard", new Vector2(80, 30)) )
                                                                                                                                                                       {
                                                                                                                                                                           ReloadConfiguration();
                                                                                                                                                                       }

                                                                                                                                                                       ImGui.EndChild();
                                                                                                                                                                   });
                                                                                         });
    }

    private void RenderNotifications()
    {
        foreach ( var notification in _notificationService.GetActiveNotifications() )
        {
            UiStyler.WithStyleColor(ImGuiCol.ChildBg, notification.GetBackgroundColor(), () =>
                                                                                         {
                                                                                             UiStyler.WithStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 5), () =>
                                                                                                                                                                   {
                                                                                                                                                                       ImGui.BeginChild($"Notification_{notification.Id}", new Vector2(ImGui.GetContentRegionAvail().X, 40), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);

                                                                                                                                                                       ImGui.AlignTextToFramePadding();
                                                                                                                                                                       ImGui.TextColored(notification.GetTextColor(), notification.Message);

                                                                                                                                                                       if ( notification.HasAction )
                                                                                                                                                                       {
                                                                                                                                                                           ImGui.SameLine(ImGui.GetContentRegionAvail().X - 100);

                                                                                                                                                                           if ( ImGui.Button(notification.ActionLabel, new Vector2(90, 30)) )
                                                                                                                                                                           {
                                                                                                                                                                               notification.InvokeAction();
                                                                                                                                                                           }
                                                                                                                                                                       }

                                                                                                                                                                       ImGui.EndChild();
                                                                                                                                                                   });
                                                                                         });
        }
    }

    private unsafe void RenderSectionTabs()
    {
        var        sections   = _sectionRegistry.GetSections();
        const int  bufferSize = 256;
        var        buffer     = stackalloc byte[bufferSize];
        StrBuilder sb         = new(buffer, bufferSize);

        foreach ( var section in sections )
        {
            sb.Append("ScrollRegion");
            sb.Append("##");
            sb.Append(section.DisplayName);
            sb.End();

            ImGui.SetNextWindowSize(new Vector2(600, 0));
            if ( ImGui.Begin(section.DisplayName, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize) )
            {
                if ( ImGui.CollapsingHeader(section.DisplayName, ImGuiTreeNodeFlags.None) )
                {
                    var availWidth = ImGui.GetContentRegionAvail().X;
                    ImGui.BeginChild(sb, new Vector2(availWidth, 0), ImGuiChildFlags.AutoResizeY, ImGuiWindowFlags.HorizontalScrollbar);
                    section.Render();
                    ImGui.EndChild();
                }
            }

            ImGui.End();

            sb.Reset();
        }
    }

    #endregion

    #region Event Handlers

    private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
    {
        // Notify relevant sections about configuration changes
        foreach ( var section in _sectionRegistry.GetSections() )
        {
            if ( section.SectionKey == e.SectionKey || e.SectionKey == null )
            {
                section.OnConfigurationChanged(e);
            }
        }
    }

    private void OnStateChanged(object sender, EditorStateChangedEventArgs e)
    {
        // Handle state changes that affect the main component
    }

    #endregion

    #region Helper Methods

    private void SaveConfiguration()
    {
        try
        {
            _configManager.SaveConfiguration();
            _notificationService.ShowSuccess("Configuration saved successfully");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"Failed to save configuration: {ex.Message}");
        }
    }

    private void ReloadConfiguration()
    {
        try
        {
            _configManager.ReloadConfiguration();
            _notificationService.ShowInfo("Configuration reloaded");
        }
        catch (Exception ex)
        {
            _notificationService.ShowError($"Failed to reload configuration: {ex.Message}");
        }
    }

    #endregion
}