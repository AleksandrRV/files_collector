using FilesCollector.Infrastructure;
using Xunit;
using FluentAssertions;

namespace FilesCollector.Tests;

public sealed class ScanRootProviderTests
{
    [Fact]
    public void Default_root_is_the_parent_of_the_application_directory()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));
        var applicationDirectory = Path.Combine(workspacePath, "files-collector");
        var paths = new AppPaths(Path.Combine(applicationDirectory, "FilesCollector.exe"), Path.Combine(workspacePath, "local-data"));
        var provider = new ScanRootProvider(paths);

        var root = provider.GetDefaultRoot();

        root.Should().Be(workspacePath);
    }

    [Fact]
    public void Application_directory_is_detected_only_when_it_is_inside_the_root()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));
        var applicationDirectory = Path.Combine(workspacePath, "files-collector");
        var paths = new AppPaths(Path.Combine(applicationDirectory, "FilesCollector.exe"), Path.Combine(workspacePath, "local-data"));
        var provider = new ScanRootProvider(paths);

        provider.IsApplicationDirectoryInsideRoot(workspacePath).Should().BeTrue();
        provider.IsApplicationDirectoryInsideRoot(Path.Combine(workspacePath, "other-root")).Should().BeFalse();
    }
}
