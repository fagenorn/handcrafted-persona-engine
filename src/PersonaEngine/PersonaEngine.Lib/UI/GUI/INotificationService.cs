namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Provides notification services for the UI
/// </summary>
public interface INotificationService
{
    void ShowInfo(string message, Action? action = null, string actionLabel = "OK");

    void ShowSuccess(string message, Action? action = null, string actionLabel = "OK");

    void ShowWarning(string message, Action? action = null, string actionLabel = "OK");

    void ShowError(string message, Action? action = null, string actionLabel = "OK");

    IReadOnlyList<Notification> GetActiveNotifications();

    void Update(float deltaTime);
}
