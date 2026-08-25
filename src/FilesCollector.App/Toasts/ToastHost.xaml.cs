using System.Windows;
using System.Windows.Controls;

namespace FilesCollector.App.Toasts;

public partial class ToastHost : UserControl
{
    public ToastHost()
    {
        InitializeComponent();
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToastMessage toast } && toast.Action is { } action && DataContext is ToastService service)
        {
            service.Remove(toast);
            action();
        }
    }

    private void OnSecondaryActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToastMessage toast } && toast.SecondaryAction is { } action && DataContext is ToastService service)
        {
            service.Remove(toast);
            action();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToastMessage toast } && DataContext is ToastService service)
        {
            service.Remove(toast);
        }
    }
}
