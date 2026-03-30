namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Implements notification service
/// </summary>
public class NotificationService : INotificationService
{
    private readonly List<Notification> _activeNotifications = new();

    public void ShowInfo(string message, Action? action = null, string actionLabel = "OK")
    {
        AddNotification(
            new Notification(Notification.NotificationType.Info, message, 5f, action, actionLabel)
        );
    }

    public void ShowSuccess(string message, Action? action = null, string actionLabel = "OK")
    {
        AddNotification(
            new Notification(
                Notification.NotificationType.Success,
                message,
                5f,
                action,
                actionLabel
            )
        );
    }

    public void ShowWarning(string message, Action? action = null, string actionLabel = "OK")
    {
        AddNotification(
            new Notification(
                Notification.NotificationType.Warning,
                message,
                8f,
                action,
                actionLabel
            )
        );
    }

    public void ShowError(string message, Action? action = null, string actionLabel = "OK")
    {
        AddNotification(
            new Notification(Notification.NotificationType.Error, message, 10f, action, actionLabel)
        );
    }

    public IReadOnlyList<Notification> GetActiveNotifications()
    {
        return _activeNotifications.AsReadOnly();
    }

    public void Update(float deltaTime)
    {
        // Update remaining time for each notification
        for (var i = _activeNotifications.Count - 1; i >= 0; i--)
        {
            var notification = _activeNotifications[i];
            notification.RemainingTime -= deltaTime;

            // Remove expired notifications
            if (notification.RemainingTime <= 0)
            {
                _activeNotifications.RemoveAt(i);
            }
        }
    }

    private void AddNotification(Notification notification)
    {
        // Limit the number of active notifications
        if (_activeNotifications.Count >= 5)
        {
            _activeNotifications.RemoveAt(0);
        }

        _activeNotifications.Add(notification);
    }
}
