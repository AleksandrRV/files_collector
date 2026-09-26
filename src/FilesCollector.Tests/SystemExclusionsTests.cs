using FilesCollector.Core.FileSystem;
using FilesCollector.Core.Ignore;
using FilesCollector.Core.Inventory;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Presets;
using FilesCollector.Core.Rules;
using FilesCollector.Infrastructure;
using FilesCollector.Infrastructure.FileSystem;
using FilesCollector.Infrastructure.Inventory;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class SystemExclusionsTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(".git", true)]
    [InlineData(".git/config", true)]
    [InlineData("vendor/lib/.git", true)]
    [InlineData(@"vendor\lib\.GIT\HEAD", true)]
    [InlineData("tools/.svn/entries", true)]
    [InlineData(".hg/store", true)]
    [InlineData(".gitignore", false)]
    [InlineData("docs/.gitattributes", false)]
    [InlineData("src/git/Program.cs", false)]
    [InlineData("", false)]
    public void Version_control_metadata_is_detected_by_path_segment(string relativePath, bool expected)
    {
        SystemExclusions.IsVcsMetadataPath(relativePath).Should().Be(expected);
    }

    [Fact]
    public void Version_control_metadata_is_excluded_even_with_an_explicit_full_rule()
    {
        var root = Path.GetFullPath(Path.Combine(_testDirectory, "planner"));
        var inventory = new FileInventorySnapshot
        {
            RootPath = root,
            Files =
            [
                new FileInventoryEntry(Path.Combine(root, ".git", "config"), ".git/config", "[no extension]", 10, false, false, true, null),
                new FileInventoryEntry(Path.Combine(root, "src", "Program.cs"), "src/Program.cs", ".cs", 10, false, false, true, null)
            ]
        };
        var rules = new RuleSet();
        rules.SetRule(".git", PathRuleKind.Directory, CollectionMode.Full);
        rules.SetRule(".git/config", PathRuleKind.File, CollectionMode.Full);

        var plan = new CollectionPlanner(new WindowsFileSystem()).CreatePlan(inventory, rules, [], new ScanOptions());

        var gitConfig = plan.Items.Single(item => item.RelativePath == ".git/config");
        gitConfig.Mode.Should().Be(CollectionMode.Excluded);
        gitConfig.Reason.Should().Be(SystemExclusions.VcsMetadataReason);
        plan.Items.Single(item => item.RelativePath == "src/Program.cs").Mode.Should().Be(CollectionMode.Full);
    }

    [Fact]
    public void Tree_resolution_marks_version_control_metadata_as_a_system_exclusion()
    {
        var resolution = new RuleSet().Resolve(".git", PathRuleKind.Directory, SystemExclusions.IsVcsMetadataPath(".git"));

        resolution.Mode.Should().Be(CollectionMode.Excluded);
        resolution.Source.Should().Be(RuleSource.System);
    }

    [Fact]
    public void Gitignore_filter_ignores_git_metadata_implicitly()
    {
        var root = Path.GetFullPath(Path.Combine(_testDirectory, "gitignore"));
        var filter = GitIgnoreFilter.Create(Path.Combine(root, ".gitignore"), root, ["*.log", "!.git/"]);

        filter.IsIgnored(".git", isDirectory: true).Should().BeTrue();
        filter.IsIgnored(".git/HEAD", isDirectory: false).Should().BeTrue();
        filter.IsIgnored("vendor/lib/.git", isDirectory: false).Should().BeTrue();
        filter.IsIgnored(".gitignore", isDirectory: false).Should().BeFalse();
        filter.IsIgnored("src/Program.cs", isDirectory: false).Should().BeFalse();
    }

    [Fact]
    public void Inventory_does_not_traverse_version_control_directories()
    {
        var root = Path.Combine(_testDirectory, "inventory-vcs");
        WriteFile(root, ".git/objects/ab/cdef", "x");
        WriteFile(root, ".svn/entries", "x");
        WriteFile(root, "src/Program.cs", "class Program { }");
        WriteFile(root, "vendor/lib/.git", "gitdir: ../../.git/modules/lib");

        var snapshot = CreateInventoryStore().Refresh(root, null, false, null, CancellationToken.None);

        snapshot.Files.Select(file => file.RelativePath).Should().BeEquivalentTo("src/Program.cs", "vendor/lib/.git");
        snapshot.SchemaVersion.Should().Be(FileInventorySnapshot.CurrentSchemaVersion);
    }

    [Fact]
    public void Inventory_files_inherit_the_hidden_attribute_of_their_folders()
    {
        var root = Path.Combine(_testDirectory, "inventory-hidden");
        var hiddenName = OperatingSystem.IsWindows() ? "private" : ".private";
        WriteFile(root, hiddenName + "/nested/token.txt", "secret");
        WriteFile(root, "public/readme.txt", "hello");
        if (OperatingSystem.IsWindows())
        {
            var hiddenDirectory = Path.Combine(root, hiddenName);
            File.SetAttributes(hiddenDirectory, File.GetAttributes(hiddenDirectory) | FileAttributes.Hidden);
        }

        var snapshot = CreateInventoryStore().Refresh(root, null, false, null, CancellationToken.None);
        var plan = new CollectionPlanner(new WindowsFileSystem()).CreatePlan(snapshot, new RuleSet(), [], new ScanOptions { IncludeHidden = false });

        snapshot.Files.Single(file => file.RelativePath.EndsWith("token.txt", StringComparison.Ordinal)).IsHidden.Should().BeTrue();
        snapshot.Files.Single(file => file.RelativePath == "public/readme.txt").IsHidden.Should().BeFalse();
        plan.Items.Single(item => item.RelativePath.EndsWith("token.txt", StringComparison.Ordinal)).Reason.Should().Be("hidden_file");
        plan.Items.Single(item => item.RelativePath == "public/readme.txt").Mode.Should().Be(CollectionMode.Full);
    }

    [Fact]
    public void Inventory_cache_of_an_older_schema_is_discarded()
    {
        var root = Path.Combine(_testDirectory, "inventory-schema");
        WriteFile(root, "a.txt", "a");
        var store = CreateInventoryStore();
        store.Refresh(root, null, false, null, CancellationToken.None);
        var cachePath = Directory.EnumerateFiles(Path.Combine(_testDirectory, "local", "FilesCollector", "cache"), "inventory-*.json").Single();
        File.WriteAllText(cachePath, File.ReadAllText(cachePath).Replace(
            $"\"SchemaVersion\":{FileInventorySnapshot.CurrentSchemaVersion}",
            "\"SchemaVersion\":1",
            StringComparison.Ordinal));

        store.Load(root).Should().BeNull("version 1 caches did not inherit hidden attributes and listed .git contents");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_testDirectory))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(_testDirectory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directory, FileAttributes.Directory);
        }

        Directory.Delete(_testDirectory, true);
    }

    private JsonFileInventoryStore CreateInventoryStore()
    {
        var appPaths = new AppPaths(Path.Combine(_testDirectory, "files-collector", "FilesCollector.exe"), Path.Combine(_testDirectory, "local"));
        return new JsonFileInventoryStore(appPaths, new WindowsFileSystem());
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
