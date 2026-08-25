using FilesCollector.Core.FileSystem;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Presets;
using FilesCollector.Core.Rules;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class CollectionPlannerTests
{
    [Fact]
    public void Disabled_extension_is_excluded_unless_the_file_has_an_explicit_rule()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesCollectorTests", "planner"));
        var filePath = Path.Combine(root, "app", "settings.json");
        var fileSystem = new TestFileSystem(new Dictionary<string, IReadOnlyList<FileSystemEntry>>
        {
            [root] = [DirectoryEntry(Path.Combine(root, "app"))],
            [Path.Combine(root, "app")] = [FileEntry(filePath)]
        });
        var rules = new RuleSet();
        var planner = new CollectionPlanner(fileSystem);
        var options = new ScanOptions { IncludeAllExtensions = false };
        var extensionRules = new[] { new ExtensionRule(".json", false, CollectionMode.Excluded) };

        var excludedPlan = planner.CreatePlan(root, null, rules, extensionRules, options);
        rules.SetRule("app/settings.json", PathRuleKind.File, CollectionMode.Full);
        var explicitPlan = planner.CreatePlan(root, null, rules, extensionRules, options);

        excludedPlan.Items.Should().ContainSingle().Which.Mode.Should().Be(CollectionMode.Excluded);
        excludedPlan.Items.Should().ContainSingle().Which.Reason.Should().Be("extension_disabled");
        explicitPlan.Items.Should().ContainSingle().Which.Mode.Should().Be(CollectionMode.Full);
    }

    [Fact]
    public void Exclude_pattern_marks_matching_files_as_excluded()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesCollectorTests", "planner-pattern"));
        var filePath = Path.Combine(root, "app", "node_modules", "library.js");
        var fileSystem = new TestFileSystem(new Dictionary<string, IReadOnlyList<FileSystemEntry>>
        {
            [root] = [DirectoryEntry(Path.Combine(root, "app"))],
            [Path.Combine(root, "app")] = [DirectoryEntry(Path.Combine(root, "app", "node_modules"))],
            [Path.Combine(root, "app", "node_modules")] = [FileEntry(filePath)]
        });
        var planner = new CollectionPlanner(fileSystem);
        var options = new ScanOptions { ExcludePatterns = ["**/node_modules/**"] };

        var plan = planner.CreatePlan(root, null, new RuleSet(), [], options);

        plan.Items.Should().ContainSingle().Which.Mode.Should().Be(CollectionMode.Excluded);
        plan.Items.Should().ContainSingle().Which.Reason.Should().Be("excluded_pattern");
    }

    [Fact]
    public void Large_file_is_listed_with_a_size_limit_reason()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesCollectorTests", "planner-size"));
        var filePath = Path.Combine(root, "large.txt");
        var fileSystem = new TestFileSystem(new Dictionary<string, IReadOnlyList<FileSystemEntry>>
        {
            [root] = [FileEntry(filePath, 2048)]
        });
        var planner = new CollectionPlanner(fileSystem);
        var options = new ScanOptions { MaxFileSizeBytes = 1024 };

        var plan = planner.CreatePlan(root, null, new RuleSet(), [], options);

        plan.Items.Should().ContainSingle().Which.Mode.Should().Be(CollectionMode.Listed);
        plan.Items.Should().ContainSingle().Which.Reason.Should().Be("size_limit");
    }

    [Fact]
    public void Exclude_pattern_without_a_slash_matches_files_in_any_directory()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesCollectorTests", "planner-basename"));
        var nestedFilePath = Path.Combine(root, "logs", "app", "trace.log");
        var fileSystem = new TestFileSystem(new Dictionary<string, IReadOnlyList<FileSystemEntry>>
        {
            [root] = [DirectoryEntry(Path.Combine(root, "logs"))],
            [Path.Combine(root, "logs")] = [DirectoryEntry(Path.Combine(root, "logs", "app"))],
            [Path.Combine(root, "logs", "app")] = [FileEntry(nestedFilePath)]
        });
        var planner = new CollectionPlanner(fileSystem);
        var options = new ScanOptions { ExcludePatterns = ["*.log"] };

        var plan = planner.CreatePlan(root, null, new RuleSet(), [], options);

        plan.Items.Should().ContainSingle().Which.Mode.Should().Be(CollectionMode.Excluded);
        plan.Items.Should().ContainSingle().Which.Reason.Should().Be("excluded_pattern");
    }

    private static FileSystemEntry DirectoryEntry(string path)
    {
        return new FileSystemEntry(path, Path.GetFileName(path), EntryKind.Directory, false, false, false, null, true, null);
    }

    private static FileSystemEntry FileEntry(string path, long sizeBytes = 100)
    {
        return new FileSystemEntry(path, Path.GetFileName(path), EntryKind.File, false, false, false, sizeBytes, true, null);
    }

    private sealed class TestFileSystem : IFileSystem
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<FileSystemEntry>> _entries;

        public TestFileSystem(IReadOnlyDictionary<string, IReadOnlyList<FileSystemEntry>> entries)
        {
            _entries = entries;
        }

        public DirectoryReadResult GetChildren(string directoryPath, string? excludedDirectoryPath)
        {
            return _entries.TryGetValue(directoryPath, out var entries)
                ? new DirectoryReadResult(entries, null, null)
                : new DirectoryReadResult([], null, null);
        }
    }
}
