using FilesCollector.App;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

/// <summary>Regression tests for B4 (opening reports), B5 (preset import) and B7 (closing).</summary>
public sealed class MainWindowViewModelStabilityTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Opening_a_deleted_history_report_drops_the_entry_instead_of_opening_it()
    {
        using var host = new ViewModelTestHost(_testDirectory);
        var reportPath = CreateReport(host, "2026.09.25_18.00.00_Default.md");
        host.ViewModel.RefreshReportHistoryCommand.Execute(null);
        var item = host.ViewModel.ReportHistory.Should().ContainSingle().Which;
        File.Delete(reportPath);
        var opened = new List<string>();
        host.ViewModel.OutputOpenRequested += (_, path) => opened.Add(path);

        host.ViewModel.OpenHistoryReportCommand.Execute(item);

        opened.Should().BeEmpty();
        host.ViewModel.ReportHistory.Should().BeEmpty();
        host.ViewModel.StatusText.Should().Contain("no longer exists");
    }

    [Fact]
    public void Opening_an_existing_history_report_requests_the_shell()
    {
        using var host = new ViewModelTestHost(_testDirectory);
        var reportPath = CreateReport(host, "2026.09.25_18.00.00_Default.md");
        host.ViewModel.RefreshReportHistoryCommand.Execute(null);
        var opened = new List<string>();
        host.ViewModel.OutputOpenRequested += (_, path) => opened.Add(path);

        host.ViewModel.OpenHistoryReportCommand.Execute(host.ViewModel.ReportHistory.Single());

        opened.Should().Equal(reportPath);
    }

    [Fact]
    public void Importing_a_file_with_null_values_reports_every_problem_and_keeps_running()
    {
        using var host = new ViewModelTestHost(_testDirectory);
        var presetCount = host.ViewModel.PresetItems.Count;
        var errors = new List<UserMessageEventArgs>();
        host.ViewModel.ErrorOccurred += (_, e) => errors.Add(e);
        SelectImportFile(host, """
            {
              "$schema": "files-collector-preset-v1",
              "name": "Broken",
              "scan": { "includePatterns": null },
              "extensions": [ { "extension": null } ]
            }
            """);

        var act = () => host.ViewModel.ImportPresetCommand.Execute(null);

        act.Should().NotThrow();
        errors.Should().ContainSingle();
        errors[0].Message.Should().Contain("$.scan.includePatterns").And.Contain("$.extensions[0].extension");
        host.ViewModel.PresetItems.Should().HaveCount(presetCount);
        host.ViewModel.StatusText.Should().StartWith("Preset import failed");
    }

    [Fact]
    public void Importing_a_preset_with_a_taken_name_adds_a_suffix()
    {
        using var host = new ViewModelTestHost(_testDirectory);
        SelectImportFile(host, """{ "$schema": "files-collector-preset-v1", "name": "Default", "extensions": [ { "extension": "PNG", "mode": "listed" } ] }""");

        host.ViewModel.ImportPresetCommand.Execute(null);

        host.ViewModel.ActivePresetName.Should().Be("Default (2)");
        host.PresetRepository.GetAll().Single(preset => preset.Name == "Default (2)")
            .ExtensionRules.Should().ContainSingle().Which.Extension.Should().Be(".png");
    }

    [Fact]
    public void Closing_without_changes_does_not_ask()
    {
        using var host = new ViewModelTestHost(_testDirectory);
        var asked = 0;
        host.ViewModel.UnsavedChangesRequested += (_, _) => asked++;

        host.ViewModel.ConfirmShutdown().Should().BeTrue();
        asked.Should().Be(0);
    }

    [Theory]
    [InlineData(UnsavedChangesDecision.Cancel, false)]
    [InlineData(UnsavedChangesDecision.Discard, true)]
    [InlineData(UnsavedChangesDecision.Save, true)]
    public void Closing_with_unsaved_preset_changes_follows_the_user_decision(UnsavedChangesDecision decision, bool expected)
    {
        using var host = new ViewModelTestHost(_testDirectory);
        host.ViewModel.IncludeHidden = true;
        host.ViewModel.IsPresetDirty.Should().BeTrue();
        host.ViewModel.UnsavedChangesRequested += (_, e) => e.Decision = decision;

        host.ViewModel.ConfirmShutdown().Should().Be(expected);

        var saved = host.PresetRepository.GetAll().Single(preset => preset.Name == "Default");
        saved.ScanOptions.IncludeHidden.Should().Be(decision == UnsavedChangesDecision.Save);
    }

    [Fact]
    public void Closing_with_unsaved_prefix_changes_can_be_cancelled()
    {
        using var host = new ViewModelTestHost(_testDirectory);
        var prefixes = host.ViewModel.PrefixPresets;
        prefixes.NameRequested += (_, e) =>
        {
            e.Name = "Review";
            e.IsAccepted = true;
        };
        prefixes.NewCommand.Execute(null);
        prefixes.Content = "Unsaved instructions";
        prefixes.IsDirty.Should().BeTrue();
        prefixes.UnsavedChangesRequested += (_, e) => e.Decision = UnsavedChangesDecision.Cancel;
        host.ViewModel.UnsavedChangesRequested += (_, e) => e.Decision = UnsavedChangesDecision.Save;

        host.ViewModel.ConfirmShutdown().Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private static string CreateReport(ViewModelTestHost host, string fileName)
    {
        Directory.CreateDirectory(host.AppPaths.OutputsDirectory);
        var path = Path.Combine(host.AppPaths.OutputsDirectory, fileName);
        File.WriteAllText(path, "# Files Collector report\n");
        return path;
    }

    private void SelectImportFile(ViewModelTestHost host, string json)
    {
        var path = Path.Combine(_testDirectory, "import.preset.json");
        File.WriteAllText(path, json);
        host.ViewModel.PresetImportRequested += (_, e) =>
        {
            e.FilePath = path;
            e.IsAccepted = true;
        };
    }
}
