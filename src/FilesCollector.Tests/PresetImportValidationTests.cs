using FilesCollector.Core.Presets;
using FilesCollector.Core.Rules;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

/// <summary>Regression tests for B5: malformed preset files must produce clear errors, never crashes.</summary>
public sealed class PresetImportValidationTests
{
    [Fact]
    public void Null_values_are_reported_with_their_json_paths()
    {
        var errors = GetErrors("""
            {
              "$schema": "files-collector-preset-v1",
              "name": null,
              "scan": { "includePatterns": null, "excludePatterns": [ "**/bin/**", null ], "includeHidden": null },
              "extensions": [ { "extension": null } ],
              "paths": [ { "path": null } ]
            }
            """);

        errors.Select(error => error.Path).Should().BeEquivalentTo(
            "$.name",
            "$.scan.includePatterns",
            "$.scan.excludePatterns[1]",
            "$.scan.includeHidden",
            "$.extensions[0].extension",
            "$.paths[0].path");
    }

    [Fact]
    public void Wrong_types_and_ranges_are_reported()
    {
        var errors = GetErrors("""
            {
              "$schema": "files-collector-preset-v1",
              "defaultMode": 1,
              "scan": { "maxFileSizeKiB": 0, "inventoryRefreshMinutes": 1.5, "binaryFileMode": "Binary", "redactRootPath": "yes" },
              "extensions": {},
              "paths": [ "src" ]
            }
            """);

        errors.Should().HaveCount(7);
        errors.Should().Contain(error => error.Path == "$.defaultMode" && error.Message.Contains("\"Signatures\""));
        errors.Should().Contain(error => error.Path == "$.scan.maxFileSizeKiB" && error.Message.Contains("out of range"));
        errors.Should().Contain(error => error.Path == "$.scan.inventoryRefreshMinutes" && error.Message.Contains("whole number"));
        errors.Should().Contain(error => error.Path == "$.scan.binaryFileMode");
        errors.Should().Contain(error => error.Path == "$.scan.redactRootPath");
        errors.Should().Contain(error => error.Path == "$.extensions" && error.Message.Contains("an array"));
        errors.Should().Contain(error => error.Path == "$.paths[0]");
    }

    [Fact]
    public void Numeric_strings_are_not_accepted_as_modes()
    {
        GetErrors("""{ "$schema": "files-collector-preset-v1", "defaultMode": "2" }""")
            .Should().ContainSingle().Which.Path.Should().Be("$.defaultMode");
    }

    [Fact]
    public void Unknown_properties_are_errors_with_a_suggestion()
    {
        var error = GetErrors("""{ "$schema": "files-collector-preset-v1", "scan": { "exludePatterns": [] } }""")
            .Should().ContainSingle().Which;

        error.Path.Should().Be("$.scan.exludePatterns");
        error.Message.Should().Contain("did you mean \"excludePatterns\"");
    }

    [Fact]
    public void Property_names_are_case_insensitive_but_must_be_unique()
    {
        GetErrors("""{ "$SCHEMA": "files-collector-preset-v1", "Name": "Demo", "SCAN": { "IncludeHidden": true } }""").Should().BeEmpty();
        GetErrors("""{ "$schema": "files-collector-preset-v1", "name": "A", "Name": "B" }""")
            .Should().ContainSingle().Which.Message.Should().Contain("more than once");
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("src/../../etc")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData("src//double")]
    [InlineData("bad|name.txt")]
    public void Paths_must_stay_relative_to_the_scan_root(string path)
    {
        var json = $$"""{ "$schema": "files-collector-preset-v1", "paths": [ { "path": {{System.Text.Json.JsonSerializer.Serialize(path)}}, "type": "File" } ] }""";

        GetErrors(json).Should().ContainSingle().Which.Path.Should().Be("$.paths[0].path");
    }

    [Fact]
    public void Empty_path_is_the_root_and_only_valid_for_directory_rules()
    {
        GetErrors("""{ "$schema": "files-collector-preset-v1", "paths": [ { "path": "", "type": "Directory", "mode": "Excluded" } ] }""").Should().BeEmpty();
        GetErrors("""{ "$schema": "files-collector-preset-v1", "paths": [ { "path": "", "type": "File" } ] }""").Should().ContainSingle();
    }

    [Fact]
    public void Duplicate_rules_are_reported()
    {
        var errors = GetErrors("""
            {
              "$schema": "files-collector-preset-v1",
              "extensions": [ { "extension": ".PNG" }, { "extension": "png" } ],
              "paths": [ { "path": "src/", "type": "Directory" }, { "path": "src", "type": "Directory" }, { "path": "src", "type": "File" } ]
            }
            """);

        errors.Select(error => error.Path).Should().BeEquivalentTo("$.extensions[1].extension", "$.paths[1].path");
    }

    [Theory]
    [InlineData(".tar.gz")]
    [InlineData(".")]
    [InlineData("c*s")]
    public void Invalid_extensions_are_reported(string extension)
    {
        GetErrors($$"""{ "$schema": "files-collector-preset-v1", "extensions": [ { "extension": "{{extension}}" } ] }""")
            .Should().ContainSingle().Which.Path.Should().Be("$.extensions[0].extension");
    }

    [Fact]
    public void Names_with_characters_forbidden_in_file_names_are_reported()
    {
        GetErrors("""{ "$schema": "files-collector-preset-v1", "name": "Review: backend?" }""")
            .Should().ContainSingle().Which.Path.Should().Be("$.name");
    }

    [Fact]
    public void Deserialize_collects_all_problems_into_one_exception()
    {
        var act = () => PresetExportData.Deserialize("""{ "$schema": "files-collector-preset-v1", "defaultMode": "Nope", "scan": { "maxFileSizeKiB": -1 } }""");

        var exception = act.Should().Throw<InvalidDataException>().Which;
        PresetImportException.GetErrors(exception).Should().HaveCount(2);
        exception.Message.Should().Contain("2 problems").And.Contain("$.defaultMode").And.Contain("$.scan.maxFileSizeKiB");
    }

    [Fact]
    public void Broken_json_reports_the_line()
    {
        var act = () => PresetExportData.Deserialize("{\n  \"$schema\": \"files-collector-preset-v1\",\n  \"name\": \n}");

        act.Should().Throw<InvalidDataException>().WithMessage("*not valid JSON (line 4*");
    }

    [Fact]
    public void Comments_and_trailing_commas_are_accepted_in_hand_edited_files()
    {
        var data = PresetExportData.Deserialize("""
            {
              // exported from the laptop
              "$schema": "files-collector-preset-v1",
              "name": "Edited",
              "extensions": [ { "extension": "PNG", "mode": "listed", }, ],
            }
            """);

        var preset = data.ToPreset("Edited", @"C:\roots\demo");
        preset.ExtensionRules.Should().ContainSingle().Which.Should().Be(new ExtensionRule(".png", true, CollectionMode.Listed));
    }

    [Fact]
    public void Every_export_can_be_imported_again()
    {
        var preset = new Preset
        {
            Name = "Everything",
            GlobalMode = CollectionMode.Listed,
            ExtensionRules = [new ExtensionRule("[no extension]", false, CollectionMode.Full), new ExtensionRule(".cs", true, CollectionMode.Signatures)],
            PathRules = [new PathRule(string.Empty, PathRuleKind.Directory, CollectionMode.Excluded), new PathRule("src/App.cs", PathRuleKind.File, CollectionMode.Full)],
            ScanOptions = new ScanOptions
            {
                MaxFileSizeBytes = long.MaxValue,
                InventoryRefreshMinutes = int.MaxValue,
                GitIgnorePath = @"C:\roots\demo\.gitignore"
            }
        };

        var restored = PresetExportData.Deserialize(PresetExportData.FromPreset(preset).Serialize()).ToPreset("Restored", @"C:\roots\demo");

        restored.PathRules.Should().BeEquivalentTo(preset.PathRules);
        restored.ExtensionRules.Should().BeEquivalentTo(preset.ExtensionRules);
        restored.ScanOptions.MaxFileSizeBytes.Should().Be(PresetImportValidator.MaxFileSizeKiB * 1024);
        restored.ScanOptions.InventoryRefreshMinutes.Should().Be(PresetImportValidator.MaxInventoryRefreshMinutes);
    }

    private static IReadOnlyList<PresetImportError> GetErrors(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return PresetImportValidator.Validate(document.RootElement);
    }
}
