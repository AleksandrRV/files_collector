using FilesCollector.Infrastructure;
using FilesCollector.Infrastructure.FileSystem;
using FilesCollector.Infrastructure.Inventory;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

/// <summary>
/// Regression tests for B12. Creating symbolic links needs Developer Mode or elevation on
/// Windows; without that permission the tests return early instead of failing.
/// </summary>
public sealed class ReparsePointInventoryTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Links_are_traversed_only_when_enabled()
    {
        var root = Path.Combine(_testDirectory, "root");
        var shared = Path.Combine(_testDirectory, "shared");
        WriteFile(Path.Combine(root, "own.txt"));
        WriteFile(Path.Combine(shared, "lib.txt"));
        if (!TryCreateLink(Path.Combine(root, "linked"), shared))
        {
            return;
        }

        var store = CreateStore();
        var withoutLinks = store.Refresh(root, null, false, null, CancellationToken.None);
        var withLinks = store.Refresh(root, null, true, null, CancellationToken.None);

        withoutLinks.Files.Select(file => file.RelativePath).Should().BeEquivalentTo("own.txt");
        withLinks.Files.Select(file => file.RelativePath).Should().BeEquivalentTo("own.txt", "linked/lib.txt");
        withLinks.FollowsReparsePoints.Should().BeTrue();
    }

    [Fact]
    public void A_link_to_an_ancestor_does_not_loop_forever()
    {
        var root = Path.Combine(_testDirectory, "cycle");
        WriteFile(Path.Combine(root, "sub", "file.txt"));
        if (!TryCreateLink(Path.Combine(root, "sub", "back"), root))
        {
            return;
        }

        var snapshot = CreateStore().Refresh(root, null, true, null, CancellationToken.None);

        snapshot.Files.Select(file => file.RelativePath).Should().BeEquivalentTo("sub/file.txt");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private JsonFileInventoryStore CreateStore()
    {
        var appPaths = new AppPaths(Path.Combine(_testDirectory, "files-collector", "FilesCollector.exe"), Path.Combine(_testDirectory, "local"));
        return new JsonFileInventoryStore(appPaths, new WindowsFileSystem());
    }

    private static void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    private static bool TryCreateLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
