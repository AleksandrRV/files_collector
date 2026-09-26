using System.Runtime.CompilerServices;
using System.Text.Json;
using FilesCollector.Core.Presets;
using FilesCollector.Core.Rules;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

/// <summary>
/// Keeps <c>docs/schemas/files-collector-preset-v1.schema.json</c> in sync with the
/// export data classes and the validator limits.
/// </summary>
public sealed class PresetSchemaTests
{
    private static readonly Lazy<JsonDocument> Schema = new(() => JsonDocument.Parse(File.ReadAllText(GetSchemaPath())));

    private static JsonElement Root => Schema.Value.RootElement;

    [Fact]
    public void Schema_declares_exactly_the_properties_of_the_export_format()
    {
        PropertyNames(Root).Should().BeEquivalentTo(PresetImportValidator.PropertyNamesBySection["root"]);
        PropertyNames(Root.GetProperty("properties").GetProperty("scan")).Should().BeEquivalentTo(PresetImportValidator.PropertyNamesBySection["scan"]);
        PropertyNames(Item("extensions")).Should().BeEquivalentTo(PresetImportValidator.PropertyNamesBySection["extension"]);
        PropertyNames(Item("paths")).Should().BeEquivalentTo(PresetImportValidator.PropertyNamesBySection["path"]);
    }

    [Fact]
    public void Schema_enumerations_match_the_code()
    {
        Strings(Root.GetProperty("$defs").GetProperty("collectionMode").GetProperty("enum")).Should().Equal(Enum.GetNames<CollectionMode>());
        Strings(Item("paths").GetProperty("properties").GetProperty("type").GetProperty("enum")).Should().BeEquivalentTo(Enum.GetNames<PathRuleKind>());
        Root.GetProperty("properties").GetProperty("$schema").GetProperty("const").GetString().Should().Be(PresetExportData.CurrentSchema);
    }

    [Fact]
    public void Schema_limits_match_the_validator()
    {
        var scan = Root.GetProperty("properties").GetProperty("scan").GetProperty("properties");
        scan.GetProperty("maxFileSizeKiB").GetProperty("maximum").GetInt64().Should().Be(PresetImportValidator.MaxFileSizeKiB);
        scan.GetProperty("inventoryRefreshMinutes").GetProperty("maximum").GetInt32().Should().Be(PresetImportValidator.MaxInventoryRefreshMinutes);
        Root.GetProperty("properties").GetProperty("name").GetProperty("maxLength").GetInt32().Should().Be(PresetImportValidator.MaxNameLength);
    }

    private static JsonElement Item(string arrayName)
    {
        return Root.GetProperty("properties").GetProperty(arrayName).GetProperty("items");
    }

    private static IEnumerable<string> PropertyNames(JsonElement objectSchema)
    {
        objectSchema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse("unknown properties are errors");
        return objectSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray();
    }

    private static IEnumerable<string> Strings(JsonElement array)
    {
        return array.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static string GetSchemaPath([CallerFilePath] string sourceFilePath = "")
    {
        const string fileName = "files-collector-preset-v1.schema.json";
        var sourcePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath) ?? string.Empty, "..", "..", "docs", "schemas", fileName));
        return File.Exists(sourcePath) ? sourcePath : Path.Combine(AppContext.BaseDirectory, "Schemas", fileName);
    }
}
