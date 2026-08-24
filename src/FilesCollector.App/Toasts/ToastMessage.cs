namespace FilesCollector.App.Toasts;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class ToastMessage
{
    public ToastMessage(string title, string? detail, ToastKind kind, string? actionLabel, Action? action, string? secondaryActionLabel, Action? secondaryAction, TimeSpan? duration)
    {
        Title = title;
        Detail = detail;
        Kind = kind;
        ActionLabel = actionLabel;
        Action = action;
        SecondaryActionLabel = secondaryActionLabel;
        SecondaryAction = secondaryAction;
        Expiry = System.DateTime.UtcNow + (duration ?? (action is not null ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(6)));
    }

    public string Title { get; }

    public string? Detail { get; }

    public ToastKind Kind { get; }

    public string? ActionLabel { get; }

    public Action? Action { get; }

    public string? SecondaryActionLabel { get; }

    public Action? SecondaryAction { get; }

    public System.DateTime Expiry { get; }

    public bool HasAction => ActionLabel is not null;
}
