using FilesCollector.Infrastructure;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class PortableModeTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Marker_file_switches_application_paths_to_portable_storage()
    {
        var applicationDirectory = Path.Combine(_testDirectory, "files-collector");
        Directory.CreateDirectory(applicationDirectory);
        File.WriteAllText(Path.Combine(applicationDirectory, "portable.mode"), string.Empty);

        var paths = new AppPaths(Path.Combine(applicationDirectory, "FilesCollector.exe"), Path.Combine(_testDirectory, "local-data"));

        paths.IsPortableMode.Should().BeTrue();
        paths.LocalDataDirectory.Should().Be(Path.Combine(applicationDirectory, "outputs", "app-data"));
        paths.PresetsDirectory.Should().Be(Path.Combine(applicationDirectory, "outputs", "app-data", "presets"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }
}
