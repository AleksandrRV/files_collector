using System.ComponentModel;
using System.Windows.Controls;
using FilesCollector.Core.Rules;

namespace FilesCollector.App.Inspector;

public partial class DetailsView : UserControl
{
    private InspectorViewModel? _viewModel;

    public DetailsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ApplyModeRequested -= OnApplyModeRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as InspectorViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ApplyModeRequested += OnApplyModeRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ModeControl.SetSelectedMode(_viewModel.EffectiveMode);
        }
    }

    private void OnApplyModeRequested(object? sender, CollectionMode mode)
    {
        _viewModel?.ApplyModeCommand.Execute(mode);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InspectorViewModel.EffectiveMode) or nameof(InspectorViewModel.SelectedNode))
        {
            ModeControl.SetSelectedMode(_viewModel?.EffectiveMode ?? CollectionMode.Full);
        }
    }
}
