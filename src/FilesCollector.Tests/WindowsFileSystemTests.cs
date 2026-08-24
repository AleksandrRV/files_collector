using FilesCollector.Core.FileSystem;
using Xunit;
using FilesCollector.Infrastructure.FileSystem;
using FluentAssertions;

namespace FilesCollector.Tests;

public sealed class WindowsFileSystemTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetChildren_excludes_the_application_directory_and_sorts_directories_before_files()
    {
        Directory.CreateDirectory(_testDirectory);
        Directory.CreateDirectory(Path.Combine(_testDirectory, "Zulu"));
        Directory.CreateDirectory(Path.Combine(_testDirectory, "alpha"));
        Directory.CreateDirectory(Path.Combine(_testDirectory, "files-collector"));
        File.WriteAllText(Path.Combine(_testDirectory, "zeta.txt"), "zeta");
        File.WriteAllText(Path.Combine(_testDirectory, "Beta.txt"), "beta");
        var fileSystem = new WindowsFileSystem();

        var result = fileSystem.GetChildren(_testDirectory, Path.Combine(_testDirectory, "files-collector"));

        result.IsSuccessful.Should().BeTrue();
        result.Entries.Select(entry => entry.Name).Should().Equal("alpha", "Zulu", "Beta.txt", "zeta.txt");
        result.Entries.Should().OnlyContain(entry => entry.Name != "files-collector");
        result.Entries.Take(2).Should().OnlyContain(entry => entry.Kind == EntryKind.Directory);
    }

    [Fact]
    public void GetChildren_returns_a_diagnostic_for_a_missing_directory()
    {
        var fileSystem = new WindowsFileSystem();

        var result = fileSystem.GetChildren(Path.Combine(_testDirectory, "missing"), null);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorCode.Should().Be("directory_not_found");
        result.Entries.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }
}
