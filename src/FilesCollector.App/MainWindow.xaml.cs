using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FilesCollector.Core.Rules;
using Microsoft.Win32;

namespace FilesCollector.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _subscriptionsAttached;

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        _viewModel.AboutRequested += OnAboutRequested;
        _viewModel.RootChangeRequested += OnRootChangeRequested;
        _viewModel.PresetNameRequested += OnPresetNameRequested;
        _viewModel.UnsavedChangesRequested += OnUnsavedChangesRequested;
        _viewModel.DeletePresetRequested += OnDeletePresetRequested;
        _viewModel.PrefixPresets.NameRequested += OnPresetNameRequested;
        _viewModel.PrefixPresets.UnsavedChangesRequested += OnUnsavedChangesRequested;
        _viewModel.PrefixPresets.DeleteRequested += OnDeletePrefixPresetRequested;
        _viewModel.OutputOpenRequested += OnOutputOpenRequested;
        _subscriptionsAttached = true;
        DataContext = _viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_subscriptionsAttached)
        {
            _viewModel.AboutRequested -= OnAboutRequested;
            _viewModel.RootChangeRequested -= OnRootChangeRequested;
            _viewModel.PresetNameRequested -= OnPresetNameRequested;
            _viewModel.UnsavedChangesRequested -= OnUnsavedChangesRequested;
            _viewModel.DeletePresetRequested -= OnDeletePresetRequested;
            _viewModel.PrefixPresets.NameRequested -= OnPresetNameRequested;
            _viewModel.PrefixPresets.UnsavedChangesRequested -= OnUnsavedChangesRequested;
            _viewModel.PrefixPresets.DeleteRequested -= OnDeletePrefixPresetRequested;
            _viewModel.OutputOpenRequested -= OnOutputOpenRequested;
            _viewModel.Shutdown();
            _subscriptionsAttached = false;
        }

        base.OnClosed(e);
    }

    private void OnAboutRequested(object? sender, EventArgs e)
    {
        System.Windows.MessageBox.Show(
            $"Files Collector{Environment.NewLine}Version {_viewModel.ApplicationVersion}{Environment.NewLine}Storage mode: {_viewModel.StorageMode}{Environment.NewLine}{Environment.NewLine}Application folder:{Environment.NewLine}{_viewModel.ApplicationDirectory}",
            "About Files Collector",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnRootChangeRequested(object? sender, EventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the scan root.",
            InitialDirectory = _viewModel.ScanRoot,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.SetRoot(dialog.FolderName);
        }
    }

    private void OnPresetNameRequested(object? sender, PresetNameRequestEventArgs e)
    {
        var dialog = new TextInputDialog(e.Title, e.Prompt, e.InitialName)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            e.Name = dialog.Value;
            e.IsAccepted = true;
        }
    }

    private void OnUnsavedChangesRequested(object? sender, UnsavedChangesRequestEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            $"Save changes to preset '{e.PresetName}'?",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        e.Decision = result switch
        {
            MessageBoxResult.Yes => UnsavedChangesDecision.Save,
            MessageBoxResult.No => UnsavedChangesDecision.Discard,
            _ => UnsavedChangesDecision.Cancel
        };
    }

    private void OnDeletePresetRequested(object? sender, UnsavedChangesRequestEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            $"Delete preset '{e.PresetName}'? This cannot be undone.",
            "Delete preset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        e.Decision = result == MessageBoxResult.Yes
            ? UnsavedChangesDecision.Discard
            : UnsavedChangesDecision.Cancel;
    }

    private void OnDeletePrefixPresetRequested(object? sender, UnsavedChangesRequestEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            $"Delete prefix preset '{e.PresetName}'? This cannot be undone.",
            "Delete prefix preset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        e.Decision = result == MessageBoxResult.Yes
            ? UnsavedChangesDecision.Discard
            : UnsavedChangesDecision.Cancel;
    }

    private void OnOutputOpenRequested(object? sender, string outputPath)
    {
        Process.Start(new ProcessStartInfo(outputPath)
        {
            UseShellExecute = true
        });
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.S && System.Windows.Input.Keyboard.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
        {
            _viewModel.SavePresetAsCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _viewModel.SelectedNode = e.NewValue as FileTreeNode;
    }

    private void OnTreeViewItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: FileTreeNode node })
        {
            _viewModel.LoadChildren(node);
        }
    }

    private void OnTreeViewPreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FindVisualParent<TreeViewItem>(e.OriginalSource as DependencyObject) is { } treeViewItem)
        {
            treeViewItem.IsSelected = true;
            treeViewItem.Focus();
        }
    }

    private void OnApplyModeMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string modeName } && Enum.TryParse<CollectionMode>(modeName, out var mode))
        {
            _viewModel.ApplyMode(mode);
        }
    }

    private void OnResetLocalRuleMenuItemClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetLocalRuleCommand.Execute(null);
    }

    private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
