namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Manages UI theme settings
/// </summary>
public interface IUiThemeManager
{
    void ApplyTheme();

    void SetTheme(UiTheme theme);

    UiTheme GetCurrentTheme();
}
