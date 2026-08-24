using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FilesCollector.Core.Rules;

namespace FilesCollector.App.Controls;

public partial class SegmentedModeControl : UserControl
{
    private bool _suppressClick;

    public SegmentedModeControl()
    {
        InitializeComponent();
    }

    public event EventHandler<CollectionMode>? ModeSelected;

    public CollectionMode SelectedMode { get; private set; } = CollectionMode.Full;

    public void SetSelectedMode(CollectionMode mode)
    {
        SelectedMode = mode;
        _suppressClick = true;
        FullButton.IsChecked = mode == CollectionMode.Full;
        SignaturesButton.IsChecked = mode == CollectionMode.Signatures;
        ListedButton.IsChecked = mode == CollectionMode.Listed;
        ExcludedButton.IsChecked = mode == CollectionMode.Excluded;
        _suppressClick = false;
    }

    private void OnModeButtonClick(object sender, RoutedEventArgs e)
    {
        if (_suppressClick)
        {
            return;
        }

        if (sender is ToggleButton { Tag: string modeName } && Enum.TryParse<CollectionMode>(modeName, out var mode))
        {
            SetSelectedMode(mode);
            ModeSelected?.Invoke(this, mode);
        }
    }
}
