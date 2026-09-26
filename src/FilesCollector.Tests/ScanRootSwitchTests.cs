using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

/// <summary>
/// Regression tests for B8: after the scan root changes, no plan of the previous root may
/// survive, otherwise "Create report" writes the old root's files under the new name.
/// </summary>
public sealed class ScanRootSwitchTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Selecting_the_application_directory_drops_the_plan_of_the_previous_root()
    {
        using var host = new ViewModelTestHost(_testDirectory);
        var viewModel = host.ViewModel;
        viewModel.ScanRoot.Should().Be(host.ProjectRoot);
        viewModel.GenerateReportCommand.CanExecute(null).Should().BeTrue("the project root has an inventory");
        viewModel.PlanFullCount.Should().Be(1);

        viewModel.SetRoot(host.ApplicationDirectory);

        viewModel.ScanRoot.Should().Be(host.ApplicationDirectory);
        viewModel.GenerateReportCommand.CanExecute(null).Should().BeFalse();
        viewModel.RefreshInventoryCommand.CanExecute(null).Should().BeFalse();
        viewModel.PlanFullCount.Should().Be(0);
        viewModel.PreviewText.Should().Contain("Plan is not available.");
        host.Inventory.LoadedRoots.Should().NotContain(host.ApplicationDirectory);
    }

    [Fact]
    public void Switching_to_a_root_without_an_inventory_cache_drops_the_previous_plan()
    {
        using var host = new ViewModelTestHost(_testDirectory);
        var otherRoot = Path.Combine(_testDirectory, "other");
        Directory.CreateDirectory(otherRoot);
        host.ViewModel.GenerateReportCommand.CanExecute(null).Should().BeTrue();

        host.ViewModel.SetRoot(otherRoot);

        host.ViewModel.GenerateReportCommand.CanExecute(null).Should().BeFalse("the other root has no inventory yet");
        host.ViewModel.PlanFullCount.Should().Be(0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }
}
