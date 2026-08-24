using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FilesCollector.App.Dialogs;

public sealed class AboutDialog : Window
{
    public AboutDialog(string version, string storageMode, string applicationDirectory, string dataDirectory, string outputsDirectory, string documentationPath)
    {
        Title = "About Files Collector";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        MinWidth = 400;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current!.Resources["SurfaceRaisedBrush"];
        Foreground = (Brush)Application.Current!.Resources["TextPrimaryBrush"];

        var root = new StackPanel { Margin = new Thickness(18) };
        var heading = new TextBlock
        {
            Text = "Files Collector",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.Children.Add(heading);
        AddLine(root, $"Version {version}");
        AddLine(root, $"Storage mode: {storageMode}");
        AddSection(root, "Application folder");
        AddLine(root, applicationDirectory);
        AddSection(root, "Application data");
        AddLine(root, dataDirectory);
        AddSection(root, "Outputs");
        AddLine(root, outputsDirectory);
        AddSection(root, "Documentation");
        AddLine(root, documentationPath);

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 88,
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        okButton.Click += (_, _) => DialogResult = true;
        root.Children.Add(okButton);
        Content = root;

        Loaded += (_, _) => okButton.Focus();
    }

    private static void AddSection(StackPanel root, string text)
    {
        root.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current!.Resources["TextTertiaryBrush"],
            Margin = new Thickness(0, 12, 0, 2)
        });
    }

    private static void AddLine(StackPanel root, string text)
    {
        root.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 2)
        });
    }
}
