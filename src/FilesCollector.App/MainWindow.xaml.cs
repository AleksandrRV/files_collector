using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FilesCollector.App.Dialogs;
using FilesCollector.Core.Rules;
using Microsoft.Win32;

namespace FilesCollector.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly UiSettingsStore _uiSettings;
    private FileTreeNode? _multiAnchor;
    private bool _subscriptionsAttached;

    public MainWindow(MainWindowViewModel viewModel, UiSettingsStore uiSettings)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _uiSettings = uiSettings;
        InitializeComponent();

        WireEvents();
        _subscriptionsAttached = true;
        DataContext = _viewModel;

        Loaded += (_, _) =>
        {
            ApplyBackdropChrome();
        };
    }

    private void WireEvents()
    {
        _viewModel.AboutRequested += OnAboutRequested;
        _viewModel.RootChangeRequested += OnRootChangeRequested;
        _viewModel.PresetNameRequested += OnPresetNameRequested;
        _viewModel.UnsavedChangesRequested += OnUnsavedChangesRequested;
        _viewModel.DeletePresetRequested += OnDeletePresetRequested;
        _viewModel.OutputOpenRequested += OnOutputOpenRequested;
        _viewModel.RevealRequested += OnRevealRequested;
        _viewModel.ThemeToggleRequested += OnThemeToggleRequested;
        _viewModel.DensityToggleRequested += OnDensityToggleRequested;

        _viewModel.PrefixPresets.NameRequested += OnPresetNameRequested;
        _viewModel.PrefixPresets.UnsavedChangesRequested += OnUnsavedChangesRequested;
        _viewModel.PrefixPresets.DeleteRequested += OnDeletePrefixPresetRequested;

        _viewModel.Formats.BulkEnableRequested += (_, enable) => _viewModel.BulkSetExtensionsEnabled(GetSelectedExtensionRows(), enable);
        _viewModel.Formats.BulkModeRequested += (_, mode) => _viewModel.BulkSetExtensionsMode(GetSelectedExtensionRows(), mode);

        _viewModel.Palette.Opened += (_, _) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                PaletteBox.Focus();
                PaletteBox.SelectAll();
                PaletteList.SelectedIndex = _viewModel.Palette.Entries.Count > 0 ? 0 : -1;
            }));
        };

        IncludePatternEditor.Patterns = _viewModel.Filters.IncludePatterns;
        IncludePatternEditor.AddRequested += (_, pattern) => _viewModel.AddPattern(true, pattern);
        IncludePatternEditor.RemoveRequested += (_, item) => _viewModel.RemovePattern(true, item);
        IncludePatternEditor.ToggleRequested += (_, item) => _viewModel.TogglePattern(true, item);

        ExcludePatternEditor.Patterns = _viewModel.Filters.ExcludePatterns;
        ExcludePatternEditor.HideQuickChips();
        ExcludePatternEditor.AddRequested += (_, pattern) => _viewModel.AddPattern(false, pattern);
        ExcludePatternEditor.RemoveRequested += (_, item) => _viewModel.RemovePattern(false, item);
        ExcludePatternEditor.ToggleRequested += (_, item) => _viewModel.TogglePattern(false, item);
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
            _viewModel.OutputOpenRequested -= OnOutputOpenRequested;
            _viewModel.RevealRequested -= OnRevealRequested;
            _viewModel.ThemeToggleRequested -= OnThemeToggleRequested;
            _viewModel.DensityToggleRequested -= OnDensityToggleRequested;
            _viewModel.PrefixPresets.NameRequested -= OnPresetNameRequested;
            _viewModel.PrefixPresets.UnsavedChangesRequested -= OnUnsavedChangesRequested;
            _viewModel.PrefixPresets.DeleteRequested -= OnDeletePrefixPresetRequested;
            _viewModel.Shutdown();
            _subscriptionsAttached = false;
        }

        base.OnClosed(e);
    }

    // ===== Theme / density / backdrop =====

    private void OnThemeToggleRequested(object? sender, EventArgs e)
    {
        var next = ThemeManager.IsDark ? ThemeMode.Light : ThemeMode.Dark;
        ThemeManager.ApplyTheme(next);
        _uiSettings.Save(_uiSettings.Settings with { Theme = next });
        ApplyBackdropChrome();
    }

    private void OnDensityToggleRequested(object? sender, EventArgs e)
    {
        var next = ThemeManager.AppliedDensity == DensityMode.Compact ? DensityMode.Comfortable : DensityMode.Compact;
        ThemeManager.ApplyDensity(next);
        _uiSettings.Save(_uiSettings.Settings with { Density = next });
    }

    private void ApplyBackdropChrome()
    {
        try
        {
            WindowBackdrop.TryApplyMica(this, ThemeManager.IsDark);
        }
        catch (Exception)
        {
            // Mica is a progressive enhancement; fall back to the solid background.
            if (Background is not SolidColorBrush || !ReferenceEquals(Background, Brushes.Transparent))
            {
                Background = (Brush)Application.Current!.Resources["WindowBackgroundBrush"];
            }
        }
    }

    // ===== Drag & drop =====

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0 && Directory.Exists(paths[0]))
        {
            _viewModel.SetRoot(paths[0]);
        }
    }

    // ===== Keyboard =====

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            if (FileTree.IsKeyboardFocusWithin)
            {
                _viewModel.Root.ReloadTreeCommand.Execute(null);
            }
            else
            {
                _viewModel.Root.RefreshInventoryCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && FileTree.IsKeyboardFocusWithin && _viewModel.Tree.SelectedNode is { IsPlaceholder: false } node)
        {
            if (node.IsDirectory)
            {
                var result = ChoiceDialog.Show(this, "Exclude folder",
                    $"Exclude '{node.DisplayName}' from the report? The rule applies recursively to the whole folder.",
                    [("Exclude", true), ("Cancel", false)]);
                if (result != 0)
                {
                    e.Handled = true;
                    return;
                }
            }

            _viewModel.ApplyModeToNodes([node], CollectionMode.Excluded);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_viewModel.IsPaletteOpen)
            {
                _viewModel.ClosePaletteCommand.Execute(null);
                e.Handled = true;
            }
            else if (_viewModel.Tree.HasMultiSelection)
            {
                _viewModel.Tree.SetMultiSelection([]);
                e.Handled = true;
            }
        }
    }

    // ===== Dialogs =====

    private void OnAboutRequested(object? sender, EventArgs e)
    {
        var dialog = new AboutDialog(
            _viewModel.ApplicationVersion,
            _viewModel.StorageMode,
            _viewModel.ApplicationDirectory,
            _viewModel.DataDirectory,
            _viewModel.OutputsDirectory,
            _viewModel.DocumentationPath)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void OnRootChangeRequested(object? sender, EventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the scan root.",
            InitialDirectory = _viewModel.Root.ScanRoot,
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
        var result = ChoiceDialog.Show(this, "Unsaved changes", $"Save changes to '{e.PresetName}'?",
            [("Save", false), ("Discard", false), ("Cancel", false)]);
        e.Decision = result switch
        {
            0 => UnsavedChangesDecision.Save,
            1 => UnsavedChangesDecision.Discard,
            _ => UnsavedChangesDecision.Cancel
        };
    }

    private void OnDeletePresetRequested(object? sender, UnsavedChangesRequestEventArgs e)
    {
        var result = ChoiceDialog.Show(this, "Delete preset", $"Delete preset '{e.PresetName}'? This cannot be undone.",
            [("Delete", true), ("Cancel", false)]);
        e.Decision = result == 0 ? UnsavedChangesDecision.Discard : UnsavedChangesDecision.Cancel;
    }

    private void OnDeletePrefixPresetRequested(object? sender, UnsavedChangesRequestEventArgs e)
    {
        var result = ChoiceDialog.Show(this, "Delete prefix preset", $"Delete prefix preset '{e.PresetName}'? This cannot be undone.",
            [("Delete", true), ("Cancel", false)]);
        e.Decision = result == 0 ? UnsavedChangesDecision.Discard : UnsavedChangesDecision.Cancel;
    }

    private void OnOutputOpenRequested(object? sender, string outputPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(outputPath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _viewModel.Toasts.ShowError("Could not open the item.", exception.Message);
        }
    }

    // ===== Popups =====

    private void OnPresetChipClick(object sender, RoutedEventArgs e)
    {
        PresetPopup.IsOpen = !PresetPopup.IsOpen;
    }

    private void OnPresetPopupItemClicked(object sender, MouseButtonEventArgs e)
    {
        PresetPopup.IsOpen = false;
    }

    private void OnOverflowClick(object sender, RoutedEventArgs e)
    {
        OverflowPopup.IsOpen = !OverflowPopup.IsOpen;
    }

    private void OnOverflowItemClicked(object sender, MouseButtonEventArgs e)
    {
        OverflowPopup.IsOpen = false;
    }

    private void OnPrefixMenuClick(object sender, RoutedEventArgs e)
    {
        PrefixPopup.IsOpen = !PrefixPopup.IsOpen;
    }

    private void OnPrefixPopupItemClicked(object sender, MouseButtonEventArgs e)
    {
        PrefixPopup.IsOpen = false;
    }

    // ===== Tree selection & multi-select =====

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileTreeNode node)
        {
            _viewModel.Tree.SelectedNode = node;
        }
    }

    private void OnTreeViewItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: FileTreeNode node })
        {
            node.IsExpandedState = true;
            _viewModel.LoadChildren(node);
        }
    }

    private void OnTreeViewItemCollapsed(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: FileTreeNode node })
        {
            node.IsExpandedState = false;
        }
    }

    private void OnTreePreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<TreeViewItem>(e.OriginalSource as DependencyObject) is { } treeViewItem)
        {
            treeViewItem.IsSelected = true;
            treeViewItem.Focus();
        }
    }

    private void OnTreePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount > 1)
        {
            return;
        }

        if (FindVisualParent<TreeViewItem>(e.OriginalSource as DependencyObject) is not { } item)
        {
            return;
        }

        if (item.DataContext is not FileTreeNode node || node.IsPlaceholder)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            if (_viewModel.Tree.HasMultiSelection)
            {
                _viewModel.Tree.SetMultiSelection([]);
            }

            return;
        }

        if (modifiers == ModifierKeys.Control || (modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            var multi = _viewModel.Tree.MultiSelected.ToList();
            var exists = multi.Contains(node);
            if (exists)
            {
                multi.Remove(node);
            }
            else
            {
                multi.Add(node);
            }

            _multiAnchor = node;
            _viewModel.Tree.SetMultiSelection(multi);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Shift && _multiAnchor is { } anchor)
        {
            var flat = FlattenVisibleNodes();
            var anchorIndex = flat.IndexOf(anchor);
            var nodeIndex = flat.IndexOf(node);
            if (anchorIndex >= 0 && nodeIndex >= 0)
            {
                var (lo, hi) = anchorIndex < nodeIndex ? (anchorIndex, nodeIndex) : (nodeIndex, anchorIndex);
                _viewModel.Tree.SetMultiSelection(flat.Skip(lo).Take(hi - lo + 1).ToList());
                e.Handled = true;
            }
        }
    }

    private List<FileTreeNode> FlattenVisibleNodes()
    {
        var result = new List<FileTreeNode>();
        foreach (var root in _viewModel.Tree.RootNodes)
        {
            Flatten(root, result);
        }

        return result;

        void Flatten(FileTreeNode node, List<FileTreeNode> list)
        {
            if (node.IsPlaceholder)
            {
                return;
            }

            list.Add(node);
            if (node.IsDirectory && node.IsExpandedState && node.AreChildrenLoaded)
            {
                foreach (var child in node.Children)
                {
                    Flatten(child, list);
                }
            }
        }
    }

    // ===== Node actions =====

    private void OnQuickActionClick(object sender, RoutedEventArgs e)
    {
        if (FindVisualParent<TreeViewItem>((DependencyObject)sender) is not { DataContext: FileTreeNode node } item)
        {
            return;
        }

        if (sender is FrameworkElement { Tag: string tag })
        {
            if (tag == "reset")
            {
                _viewModel.ResetLocalRuleFor(node);
                return;
            }

            if (Enum.TryParse<CollectionMode>(tag, out var mode))
            {
                item.IsSelected = true;
                _viewModel.Tree.SelectedNode = node;
                _viewModel.ApplyModeToNodes([node], mode);
            }
        }
    }

    private FileTreeNode? GetSelectedNode()
    {
        return FileTree.SelectedItem as FileTreeNode;
    }

    private void OnContextModeClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string modeName } && GetSelectedNode() is { } node && Enum.TryParse<CollectionMode>(modeName, out var mode))
        {
            _viewModel.ApplyModeToNodes([node], mode);
        }
    }

    private void OnContextResetClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedNode() is { } node)
        {
            _viewModel.ResetLocalRuleFor(node);
        }
    }

    private void OnContextIncludePatternClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedNode() is { } node)
        {
            _viewModel.AddPattern(true, PatternFromNode(node));
        }
    }

    private void OnContextExcludePatternClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedNode() is { } node)
        {
            _viewModel.AddPattern(false, PatternFromNode(node));
        }
    }

    private static string PatternFromNode(FileTreeNode node)
    {
        var relative = string.IsNullOrEmpty(node.RelativePath) ? "." : node.RelativePath.Replace('\\', '/');
        return node.IsDirectory ? relative + "/**" : relative;
    }

    private void OnContextOpenFileClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedNode() is { IsDirectory: false } node)
        {
            TryOpenPath(node.FullPath);
        }
    }

    private void OnContextRevealClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedNode() is { } node)
        {
            TryRevealInExplorer(node.FullPath);
        }
    }

    private void OnContextCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedNode() is { } node)
        {
            CopyToClipboard(node.FullPath);
        }
    }

    private void OnContextCopyRelativePathClick(object sender, RoutedEventArgs e)
    {
        if (GetSelectedNode() is { } node)
        {
            CopyToClipboard(string.IsNullOrEmpty(node.RelativePath) ? node.FullPath : node.RelativePath.Replace('\\', '/'));
        }
    }

    // ===== Reveal in tree =====

    private void OnRevealRequested(object? sender, FileTreeNode node)
    {
        var chain = new List<FileTreeNode>();
        var current = node.Parent;
        while (current is not null)
        {
            chain.Insert(0, current);
            current = current.Parent;
        }

        foreach (var ancestor in chain)
        {
            if (ancestor.IsDirectory)
            {
                var container = FindContainer(FileTree, ancestor);
                if (container is not null)
                {
                    container.IsExpanded = true;
                }
            }
        }

        FileTree.UpdateLayout();
        var container = FindContainer(FileTree, node);
        if (container is not null)
        {
            container.IsSelected = true;
            container.BringIntoView();
        }

        _viewModel.Tree.SelectedNode = node;
    }

    private static TreeViewItem? FindContainer(ItemsControl owner, FileTreeNode target)
    {
        if (owner.Items is null)
        {
            return null;
        }

        foreach (var item in owner.Items)
        {
            var container = owner.ItemContainerGenerator.ContainerFromItem(item);
            if (container is TreeViewItem treeViewItem)
            {
                if (ReferenceEquals(treeViewItem.DataContext, target))
                {
                    return treeViewItem;
                }

                var nested = FindContainer(treeViewItem, target);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    // ===== Palette =====

    private void OnPaletteBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel.Palette.SetQuery(PaletteBox.Text);
    }

    private void OnPaletteBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.ClosePaletteCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _viewModel.Palette.ExecuteSelected();
            e.Handled = true;
        }
    }

    private void OnPaletteListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.Palette.ExecuteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.ClosePaletteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPaletteBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, PaletteOverlay))
        {
            _viewModel.ClosePaletteCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ===== Helpers =====

    private IReadOnlyList<ExtensionRow> GetSelectedExtensionRows()
    {
        return FormatsListView.SelectedItems.OfType<ExtensionRow>().ToList();
    }

    private void TryOpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _viewModel.Toasts.ShowError("Could not open the file.", exception.Message);
        }
    }

    private void TryRevealInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _viewModel.Toasts.ShowError("Could not open Explorer.", exception.Message);
        }
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            _viewModel.StatusText = "Path copied to the clipboard.";
        }
        catch (Exception)
        {
            _viewModel.Toasts.ShowWarning("The clipboard is unavailable.");
        }
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
