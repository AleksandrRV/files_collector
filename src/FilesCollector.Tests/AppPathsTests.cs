using FilesCollector.Core;
using FilesCollector.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FilesCollector.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void Constructor_builds_expected_paths_from_the_executable_location()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));
        var executablePath = Path.Combine(testRoot, "files-collector", "FilesCollector.exe");
        var localDataPath = Path.Combine(testRoot, "local-data");

        var paths = new AppPaths(executablePath, localDataPath);

        paths.ExecutablePath.Should().Be(Path.GetFullPath(executablePath));
        paths.ApplicationDirectory.Should().Be(Path.Combine(testRoot, "files-collector"));
        paths.DocumentationDirectory.Should().Be(Path.Combine(testRoot, "files-collector", "docs"));
        paths.OutputsDirectory.Should().Be(Path.Combine(testRoot, "files-collector", "outputs"));
        paths.LocalDataDirectory.Should().Be(Path.Combine(localDataPath, "FilesCollector"));
        paths.PresetsDirectory.Should().Be(Path.Combine(localDataPath, "FilesCollector", "presets"));
        paths.PrefixesDirectory.Should().Be(Path.Combine(localDataPath, "FilesCollector", "prefixes"));
        paths.CacheDirectory.Should().Be(Path.Combine(localDataPath, "FilesCollector", "cache"));
        paths.LogsDirectory.Should().Be(Path.Combine(localDataPath, "FilesCollector", "logs"));
    }

    [Fact]
    public void Service_collection_resolves_application_paths()
    {
        var executablePath = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"), "FilesCollector.exe");
        var services = new ServiceCollection();
        services.AddFilesCollectorInfrastructure(executablePath);
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var paths = provider.GetRequiredService<IAppPaths>();
        var scanRootProvider = provider.GetRequiredService<IScanRootProvider>();

        paths.Should().BeOfType<AppPaths>();
        scanRootProvider.Should().BeOfType<ScanRootProvider>();
        paths.ExecutablePath.Should().Be(Path.GetFullPath(executablePath));
    }
}
