using FilesCollector.Core.FileSystem;
using FilesCollector.Core.Inventory;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Presets;
using FilesCollector.Core.Rules;
using FilesCollector.Infrastructure.FileSystem;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

/// <summary>Regression tests for B9 (binary content in the plan) and B11 (default mode).</summary>
public sealed class PlanConsistencyTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesCollectorTests", "plan-consistency"));

    [Fact]
    public void Default_mode_applies_to_files_without_rules()
    {
        var plan = CreatePlan(new RuleSet(), [], CollectionMode.Signatures);

        plan.Items.Should().OnlyContain(item => item.Mode == CollectionMode.Signatures);
    }

    [Fact]
    public void Path_and_extension_rules_win_over_the_default_mode()
    {
        var rules = new RuleSet();
        rules.SetRule("docs", PathRuleKind.Directory, CollectionMode.Listed);
        var extensionRules = new[] { new ExtensionRule(".cs", true, CollectionMode.Full) };

        var plan = CreatePlan(rules, extensionRules, CollectionMode.Excluded);

        Mode(plan, "src/Program.cs").Should().Be(CollectionMode.Full, "the extension rule overrides the default mode");
        Mode(plan, "docs/readme.md").Should().Be(CollectionMode.Listed, "the folder rule overrides both");
        Mode(plan, "notes.txt").Should().Be(CollectionMode.Excluded, "only the default mode applies");
    }

    [Fact]
    public void Tree_resolution_uses_the_default_mode_for_global_items()
    {
        var resolution = new RuleSet().Resolve("src", PathRuleKind.Directory, defaultMode: CollectionMode.Listed);

        resolution.Mode.Should().Be(CollectionMode.Listed);
        resolution.Source.Should().Be(RuleSource.Global);
    }

    [Fact]
    public void Files_with_known_binary_content_are_listed_and_leave_the_estimate()
    {
        var probe = new FakeProbe { BinaryPaths = { Path.Combine(Root, "notes.txt") } };

        var plan = CreatePlan(new RuleSet(), [], CollectionMode.Full, probe);

        var notes = plan.Items.Single(item => item.RelativePath == "notes.txt");
        notes.Mode.Should().Be(CollectionMode.Listed);
        notes.Reason.Should().Be("binary_file");
        plan.FullCount.Should().Be(2);
        plan.EstimatedBytes.Should().Be(200, "only the two text files are rendered");
    }

    [Fact]
    public void Excluded_files_are_not_turned_into_listed_ones_by_the_probe()
    {
        var probe = new FakeProbe { BinaryPaths = { Path.Combine(Root, "notes.txt") } };

        var plan = CreatePlan(new RuleSet(), [], CollectionMode.Excluded, probe);

        Mode(plan, "notes.txt").Should().Be(CollectionMode.Excluded);
    }

    private static CollectionPlan CreatePlan(RuleSet rules, IReadOnlyList<ExtensionRule> extensionRules, CollectionMode defaultMode, IFileContentProbe? probe = null)
    {
        var inventory = new FileInventorySnapshot
        {
            RootPath = Root,
            Files =
            [
                Entry("src/Program.cs", ".cs"),
                Entry("docs/readme.md", ".md"),
                Entry("notes.txt", ".txt")
            ]
        };
        return new CollectionPlanner(new WindowsFileSystem()).CreatePlan(inventory, rules, extensionRules, new ScanOptions(), null, defaultMode, probe);
    }

    private static FileInventoryEntry Entry(string relativePath, string extension)
    {
        return new FileInventoryEntry(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)), relativePath, extension, 100, false, false, true, null);
    }

    private static CollectionMode Mode(CollectionPlan plan, string relativePath)
    {
        return plan.Items.Single(item => item.RelativePath == relativePath).Mode;
    }

    private sealed class FakeProbe : IFileContentProbe
    {
        public HashSet<string> BinaryPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool? TryGetCachedIsBinary(string fullPath, long? sizeBytes)
        {
            return BinaryPaths.Contains(fullPath);
        }

        public bool ProbeIsBinary(string fullPath, long? sizeBytes)
        {
            return BinaryPaths.Contains(fullPath);
        }
    }
}
