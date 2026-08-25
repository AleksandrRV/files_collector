using System.Collections.ObjectModel;

namespace FilesCollector.App.Toasts;

public sealed class ToastService
{
    private readonly System.Windows.Threading.DispatcherTimer _timer;

    public ToastService()
    {
        Items = [];
        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    public ObservableCollection<ToastMessage> Items { get; }

    public void Show(string title, string? detail = null, ToastKind kind = ToastKind.Info, string? actionLabel = null, Action? action = null, string? secondaryActionLabel = null, Action? secondaryAction = null, TimeSpan? duration = null)
    {
        Items.Add(new ToastMessage(title, detail, kind, actionLabel, action, secondaryActionLabel, secondaryAction, duration));
        while (Items.Count > 4)
        {
            Items.RemoveAt(0);
        }
    }

    public void ShowSuccess(string title, string? detail = null, string? actionLabel = null, Action? action = null, string? secondaryActionLabel = null, Action? secondaryAction = null)
    {
        Show(title, detail, ToastKind.Success, actionLabel, action, secondaryActionLabel, secondaryAction);
    }

    public void ShowError(string title, string? detail = null)
    {
        Show(title, detail, ToastKind.Error, duration: TimeSpan.FromSeconds(10));
    }

    public void ShowWarning(string title, string? detail = null)
    {
        Show(title, detail, ToastKind.Warning, duration: TimeSpan.FromSeconds(10));
    }

    public void Remove(ToastMessage toast)
    {
        Items.Remove(toast);
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var now = System.DateTime.UtcNow;
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].Expiry <= now)
            {
                Items.RemoveAt(i);
            }
        }
    }
}
