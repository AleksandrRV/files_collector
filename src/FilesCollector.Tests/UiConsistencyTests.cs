using FilesCollector.App;
using FilesCollector.Core.Presets;
using FilesCollector.Core.Rules;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

/// <summary>Regression tests for B9–B13, B15 and B16 on the view model.</summary>
public sealed class UiConsistencyTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Tree_shows_the_plan_outcome_and_marks_filter_decisions()
    {
        using var host = new ViewModelTestHost(_testDirectory);
        var viewModel = host.ViewModel;
        var file = ExpandToProgramFile(viewModel);
        file.EffectiveMode.Should().Be(CollectionMode.Full);
        file.SourceDisplayText.Should().Be("Global");

        viewModel.ExtensionRuleItems.Single(item => item.Extension == ".cs").Enabled = false;

        file.EffectiveMode.Should().Be(CollectionMode.Excluded);
        file.RuleMode.Should().Be(CollectionMode.Full);
        file.PlanReason.Should().Be("extension_disabled");
        file.SourceDisplayText.Should().Be("Filter");
        viewModel.PlanExcludedCount.Should().Be(1);
    }

    [Fact]
    public void Default_mode_changes_the_plan_and_marks_the_preset_as_changed()
    {
        using var host = new ViewModelTestHost(_testDirectory);

        host.ViewModel.DefaultMode = CollectionMode.Listed;

        host.ViewModel.PlanListedCount.Should().Be(1);
        host.ViewModel.PlanFullCount.Should().Be(0);
        host.ViewModel.IsPresetDirty.Should().BeTrue();
        ExpandToProgramFile(host.ViewModel).EffectiveMode.Should().Be(CollectionMode.Listed);
    }

    [Fact]
    public void Export_contains_unsaved_path_rules_and_prefix_selection()
    {
        using var host = new ViewModelTestHost(_testDirectory);
        var viewModel = host.ViewModel;
        viewModel.SelectedNode = ExpandToProgramFile(viewModel);
        viewModel.ApplyMode(CollectionMode.Signatures);
        viewModel.DefaultMode = CollectionMode.Listed;
        var exportPath = Path.Combine(_testDirectory, "export.preset.json");
        viewModel.PresetExportRequested += (_, e) =>
        {
            e.FilePath = exportPath;
            e.IsAccepted = true;
        };

        viewModel.ExportPresetCommand.Execute(null);

        var exported = PresetExportData.Deserialize(File.ReadAllText(exportPath));
        exported.DefaultMode.Should().Be(CollectionMode.Listed);
        exported.Paths.Should().ContainSingle().Which.Path.Should().Be("src/Program.cs");
    }

    [Fact]
    public void Rename_does_not_save_other_unsaved_changes()
    {
        using var host = new ViewModelTestHost(_testDirectory);
        var viewModel = host.ViewModel;
        viewModel.PresetNameRequested += (_, e) =>
        {
            e.Name = e.Title == "New preset" ? "Work" : "Work renamed";
            e.IsAccepted = true;
        };
        viewModel.NewPresetCommand.Execute(null);
        viewModel.IncludeHidden = true;
        viewModel.IsPresetDirty.Should().BeTrue();

        viewModel.RenamePresetCommand.Execute(null);

        viewModel.ActivePresetName.Should().Be("Work renamed");
        viewModel.IsPresetDirty.Should().BeTrue("the filter change is still unsaved");
        var stored = host.PresetRepository.GetAll().Single(preset => preset.Name == "Work renamed");
        stored.ScanOptions.IncludeHidden.Should().BeFalse();
        viewModel.PresetItems.Should().Contain(item => item.Name == "Work renamed");
    }

    [Theory]
    [InlineData(UnsavedChangesDecision.Cancel)]
    [InlineData(UnsavedChangesDecision.Discard)]
    public void Switching_to_a_preset_with_another_prefix_asks_about_prefix_changes_first(UnsavedChangesDecision decision)
    {
        using var host = new ViewModelTestHost(_testDirectory);
        var viewModel = host.ViewModel;
        var prefixes = viewModel.PrefixPresets;
        prefixes.NameRequested += (_, e) =>
        {
            e.Name = "Review";
            e.IsAccepted = true;
        };
        prefixes.NewCommand.Execute(null);
        viewModel.SavePresetCommand.Execute(null);
        var other = new Preset { Id = Guid.NewGuid(), Name = "Other", ScanRootPath = host.ProjectRoot };
        host.PresetRepository.Save(other);
        prefixes.Content = "Unsaved instructions";
        prefixes.UnsavedChangesRequested += (_, e) => e.Decision = decision;

        viewModel.SelectedPresetId = other.Id;

        if (decision == UnsavedChangesDecision.Cancel)
        {
            viewModel.ActivePresetName.Should().Be("Default");
            viewModel.SelectedPresetId.Should().NotBe(other.Id);
            prefixes.Content.Should().Be("Unsaved instructions");
        }
        else
        {
            viewModel.ActivePresetName.Should().Be("Other");
            prefixes.SelectedId.Should().BeNull();
            viewModel.IsPresetDirty.Should().BeFalse();
        }
    }

    [Fact]
    public void Prefix_name_conflicts_are_reported()
    {
        using var host = new ViewModelTestHost(_testDirectory);
        var prefixes = host.ViewModel.PrefixPresets;
        var errors = new List<string>();
        prefixes.ErrorOccurred += (_, message) => errors.Add(message);
        prefixes.NameRequested += (_, e) =>
        {
            e.Name = "Review";
            e.IsAccepted = true;
        };
        prefixes.NewCommand.Execute(null);

        prefixes.NewCommand.Execute(null);

        errors.Should().ContainSingle().Which.Should().Contain("already exists");
    }

    [Fact]
    public void Enabling_follow_reparse_points_refreshes_the_inventory_with_links()
    {
        using var host = new ViewModelTestHost(_testDirectory);

        host.ViewModel.FollowReparsePoints = true;

        SpinWait.SpinUntil(() => host.Inventory.RefreshFollowArguments.Contains(true), TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private static FileTreeNode ExpandToProgramFile(MainWindowViewModel viewModel)
    {
        var root = viewModel.RootNodes.Single();
        var source = root.Children.Single(node => node.DisplayName == "src");
        viewModel.LoadChildren(source);
        return source.Children.Single(node => node.DisplayName == "Program.cs");
    }
}
