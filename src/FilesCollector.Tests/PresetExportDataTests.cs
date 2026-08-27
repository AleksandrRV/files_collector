using FilesCollector.Core.Presets;
using FilesCollector.Core.Rules;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class PresetExportDataTests
{
    [Fact]
    public void Round_trip_preserves_every_editable_field()
    {
        var preset = new Preset
        {
            Id = Guid.NewGuid(),
            Name = "Backend review",
            GlobalMode = CollectionMode.Signatures,
            ExtensionRules = [new ExtensionRule(".png", true, CollectionMode.Listed)],
            PathRules =
            [
                new PathRule("app/node_modules", PathRuleKind.Directory, CollectionMode.Excluded),
                new PathRule("src/App.cs", PathRuleKind.File, CollectionMode.Full)
            ],
            ScanOptions = new ScanOptions
            {
                IncludeAllExtensions = false,
                IncludeHidden = true,
                IncludeSystem = true,
                FollowReparsePoints = true,
                MaxFileSizeBytes = 5 * 1024 * 1024,
                BinaryFileMode = CollectionMode.Excluded,
                IncludePatterns = ["src/**"],
                ExcludePatterns = ["**/node_modules/**"],
                RedactRootPath = true,
                IncludeFileMetadataBlocks = false,
                InventoryRefreshMinutes = 7,
                GitIgnorePath = @"C:\roots\demo\.gitignore"
            }
        };

        var json = PresetExportData.FromPreset(preset).Serialize();
        var restored = PresetExportData.Deserialize(json).ToPreset("Restored", @"C:\roots\demo");

        restored.Name.Should().Be("Restored");
        restored.GlobalMode.Should().Be(CollectionMode.Signatures);
        restored.Id.Should().NotBe(preset.Id);
        restored.ScanRootPath.Should().Be(@"C:\roots\demo");
        restored.ExtensionRules.Should().BeEquivalentTo(preset.ExtensionRules);
        restored.PathRules.Should().BeEquivalentTo(preset.PathRules);
        restored.ScanOptions.Should().BeEquivalentTo(preset.ScanOptions);
    }

    [Fact]
    public void Gitignore_path_is_omitted_when_the_option_is_not_used()
    {
        var json = PresetExportData.FromPreset(new Preset { Name = "Demo" }).Serialize();

        json.Should().NotContain("gitIgnorePath");
        PresetExportData.Deserialize(json).ToPreset("Demo", @"C:\roots\demo").ScanOptions.GitIgnorePath.Should().BeEmpty();
    }

    [Fact]
    public void Serialized_file_is_human_readable()
    {
        var preset = new Preset
        {
            Name = "Demo",
            PathRules = [new PathRule("build", PathRuleKind.Directory, CollectionMode.Excluded)]
        };

        var json = PresetExportData.FromPreset(preset).Serialize();

        json.Should().Contain("\"$schema\": \"files-collector-preset-v1\"");
        json.Should().Contain("\"defaultMode\": \"Full\"");
        json.Should().Contain("\"type\": \"Directory\"");
        json.Should().Contain("\"mode\": \"Excluded\"");
        json.Should().NotContain(Convert.ToChar((byte)0).ToString());
        json.Split('\n').Should().HaveCountGreaterThan(10);
    }

    [Fact]
    public void Deserialize_rejects_unknown_schema()
    {
        var act = () => PresetExportData.Deserialize("""{ "name": "x" }""");

        act.Should().Throw<InvalidDataException>().WithMessage("*$schema*");
    }

    [Fact]
    public void Deserialize_reports_broken_json_clearly()
    {
        var act = () => PresetExportData.Deserialize("{ not valid json ");

        act.Should().Throw<InvalidDataException>().WithMessage("*not valid JSON*");
    }
}
