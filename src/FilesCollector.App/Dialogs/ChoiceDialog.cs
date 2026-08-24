using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FilesCollector.App.Dialogs;

public sealed class ChoiceDialog : Window
{
    private int _resultIndex = -1;

    public ChoiceDialog(string title, string message, IReadOnlyList<(string Label, bool IsDanger)> buttons)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        MinWidth = 380;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"];
        Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"];

        var root = new StackPanel { Margin = new Thickness(18) };
        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
            FontSize = 13
        };
        root.Children.Add(messageText);

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = buttons.Count - 1; i >= 0; i--)
        {
            var index = i;
            var (label, isDanger) = buttons[i];
            var button = new Button
            {
                Content = label,
                MinWidth = 88,
                Margin = new Thickness(8, 0, 0, 0)
            };
            if (isDanger)
            {
                button.Style = (Style)Application.Current.Resources["DangerButton"];
            }
            if (index == 0)
            {
                button.IsDefault = true;
            }
            if (index == buttons.Count - 1)
            {
                button.IsCancel = true;
            }

            button.Click += (_, _) =>
            {
                _resultIndex = index;
                DialogResult = true;
            };

            buttonPanel.Children.Insert(0, button);
        }

        root.Children.Add(buttonPanel);
        Content = root;

        Loaded += (_, _) =>
        {
            var firstButton = FindFirstButton(buttonPanel);
            firstButton?.Focus();
        };
    }

    public int ResultIndex => _resultIndex;

    public static int Show(Window owner, string title, string message, IReadOnlyList<(string Label, bool IsDanger)> buttons)
    {
        var dialog = new ChoiceDialog(title, message, buttons) { Owner = owner };
        var shown = dialog.ShowDialog();
        return shown == true ? dialog.ResultIndex : -1;
    }

    private static Button? FindFirstButton(Panel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is Button button)
            {
                return button;
            }
        }

        return null;
    }
}
