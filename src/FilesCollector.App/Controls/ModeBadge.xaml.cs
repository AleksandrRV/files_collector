using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FilesCollector.Core.Rules;

namespace FilesCollector.App.Controls;

public partial class ModeBadge : UserControl
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode),
        typeof(CollectionMode),
        typeof(ModeBadge),
        new FrameworkPropertyMetadata(CollectionMode.Full, OnModeChanged));

    public ModeBadge()
    {
        InitializeComponent();
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    protected override void OnUnload(System.Windows.RoutedEventArgs e)
    {
        base.OnUnload(e);
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyColors(Mode);
    }

    public CollectionMode Mode
    {
        get => (CollectionMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public void RefreshColors()
    {
        ApplyColors(Mode);
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ModeBadge)d).ApplyColors((CollectionMode)e.NewValue);
    }

    private void ApplyColors(CollectionMode mode)
    {
        var brushKey = mode switch
        {
            CollectionMode.Full => "ModeFullBrush",
            CollectionMode.Signatures => "ModeSignaturesBrush",
            CollectionMode.Listed => "ModeListedBrush",
            CollectionMode.Excluded => "ModeExcludedBrush",
            _ => "TextTertiaryBrush"
        };

        var softKey = mode switch
        {
            CollectionMode.Full => "ModeFullSoftBrush",
            CollectionMode.Signatures => "ModeSignaturesSoftBrush",
            CollectionMode.Listed => "ModeListedSoftBrush",
            CollectionMode.Excluded => "ModeExcludedSoftBrush",
            _ => "SurfaceAltBrush"
        };

        var app = Application.Current;
        Dot.Fill = app?.TryFindResource(brushKey) as Brush ?? Brushes.Gray;
        Label.Foreground = app?.TryFindResource(brushKey) as Brush ?? Brushes.Gray;
        Badge.Background = app?.TryFindResource(softKey) as Brush ?? Brushes.Transparent;

        Label.Text = mode switch
        {
            CollectionMode.Full => "Full",
            CollectionMode.Signatures => "Signatures",
            CollectionMode.Listed => "Listed",
            CollectionMode.Excluded => "Excluded",
            _ => string.Empty
        };

        Opacity = mode == CollectionMode.Excluded ? 0.75 : 1.0;
    }
}
