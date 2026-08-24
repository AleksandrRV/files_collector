using System.Windows;
using System.Windows.Controls;

namespace FilesCollector.App;

public sealed class TextInputDialog : Window
{
    private readonly TextBox _textBox;

    public TextInputDialog(string title, string prompt, string initialValue)
    {
        Title = title;
        Width = 420;
        Height = 180;
        MinWidth = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _textBox = new TextBox
        {
            Text = initialValue,
            MinWidth = 300,
            Margin = new Thickness(0, 8, 0, 16)
        };

        var confirmButton = new Button
        {
            Content = "OK",
            MinWidth = 84,
            IsDefault = true
        };
        confirmButton.Click += OnConfirmClick;

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };

        var buttons = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Orientation = Orientation.Horizontal
        };
        buttons.Children.Add(confirmButton);
        buttons.Children.Add(cancelButton);

        var panel = new StackPanel
        {
            Margin = new Thickness(16)
        };
        panel.Children.Add(new TextBlock { Text = prompt });
        panel.Children.Add(_textBox);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
    }

    public string Value => _textBox.Text;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
