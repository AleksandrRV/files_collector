using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FilesCollector.App.Patterns;

namespace FilesCollector.App.Controls;

public partial class PatternEditor : UserControl
{
    private System.Collections.ObjectModel.ObservableCollection<PatternItem>? _patterns;

    public static readonly DependencyProperty PatternsProperty = DependencyProperty.Register(
        nameof(Patterns),
        typeof(System.Collections.ObjectModel.ObservableCollection<PatternItem>),
        typeof(PatternEditor),
        new PropertyMetadata(null, OnPatternsChanged));

    public PatternEditor()
    {
        InitializeComponent();
        PatternsList.ItemsSource = null;
    }

    public event EventHandler<string>? AddRequested;

    public event EventHandler<PatternItem>? RemoveRequested;

    public event EventHandler<PatternItem>? ToggleRequested;

    public System.Collections.ObjectModel.ObservableCollection<PatternItem>? Patterns
    {
        get => (System.Collections.ObjectModel.ObservableCollection<PatternItem>?)GetValue(PatternsProperty);
        set => SetValue(PatternsProperty, value);
    }

    private static void OnPatternsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (PatternEditor)d;
        editor._patterns = (System.Collections.ObjectModel.ObservableCollection<PatternItem>?)e.OldValue;
        editor._patterns?.CollectionChanged -= editor.OnPatternsCollectionChanged;

        var patterns = (System.Collections.ObjectModel.ObservableCollection<PatternItem>?)e.NewValue;
        editor._patterns = patterns;
        patterns?.CollectionChanged += editor.OnPatternsCollectionChanged;
        editor.PatternsList.ItemsSource = patterns;
        editor.PatternsList.Visibility = patterns is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPatternsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        PatternsList.Visibility = _patterns is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    public void HideQuickChips()
    {
        QuickChips.Visibility = Visibility.Collapsed;
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        CommitAdd();
    }

    private void CommitAdd()
    {
        var text = AddBox.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        AddBox.Clear();
        AddRequested?.Invoke(this, text);
        AddBox.Focus();
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PatternItem item })
        {
            RemoveRequested?.Invoke(this, item);
        }
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PatternItem item })
        {
            ToggleRequested?.Invoke(this, item);
        }
    }

    private void OnQuickChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string pattern })
        {
            AddRequested?.Invoke(this, pattern);
            AddBox.Focus();
        }
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.FocusedElement is TextBox)
        {
            CommitAdd();
            e.Handled = true;
        }
    }
}
