using System.Numerics;

namespace PersonaEngine.Lib.UI.GUI;

/// <summary>
///     Notification data structure
/// </summary>
public class Notification
{
    public enum NotificationType
    {
        Info,

        Success,

        Warning,

        Error,
    }

    public Notification(
        NotificationType type,
        string message,
        float duration = 5f,
        Action? action = null,
        string actionLabel = "OK"
    )
    {
        Type = type;
        Message = message;
        RemainingTime = duration;
        Action = action;
        ActionLabel = actionLabel;
    }

    public string Id { get; } = Guid.NewGuid().ToString();

    public NotificationType Type { get; }

    public string Message { get; }

    public float RemainingTime { get; set; }

    public Action? Action { get; }

    public string ActionLabel { get; }

    public bool HasAction => Action != null;

    public void InvokeAction()
    {
        Action?.Invoke();
    }

    public Vector4 GetBackgroundColor()
    {
        return Type switch
        {
            NotificationType.Info => new Vector4(0.2f, 0.4f, 0.8f, 0.2f),
            NotificationType.Success => new Vector4(0.2f, 0.8f, 0.2f, 0.2f),
            NotificationType.Warning => new Vector4(0.9f, 0.7f, 0.0f, 0.2f),
            NotificationType.Error => new Vector4(0.8f, 0.2f, 0.2f, 0.25f),
            _ => new Vector4(0.3f, 0.3f, 0.3f, 0.2f),
        };
    }

    public Vector4 GetTextColor()
    {
        return Type switch
        {
            NotificationType.Info => new Vector4(0.3f, 0.6f, 1.0f, 1.0f),
            NotificationType.Success => new Vector4(0.0f, 0.8f, 0.0f, 1.0f),
            NotificationType.Warning => new Vector4(1.0f, 0.7f, 0.0f, 1.0f),
            NotificationType.Error => new Vector4(1.0f, 0.3f, 0.3f, 1.0f),
            _ => new Vector4(0.9f, 0.9f, 0.9f, 1.0f),
        };
    }
}
