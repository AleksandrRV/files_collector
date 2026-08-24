using FilesCollector.Core.Settings;
using FilesCollector.Infrastructure;
using FilesCollector.Infrastructure.Settings;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class JsonAppSessionStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Stored_preset_id_is_available_after_store_is_recreated()
    {
        var presetId = Guid.NewGuid();
        CreateStore().SetLastPresetId(presetId);

        var restored = CreateStore().GetLastPresetId();

        restored.Should().Be(presetId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private IAppSessionStore CreateStore()
    {
        var paths = new AppPaths(Path.Combine(_testDirectory, "files-collector", "FilesCollector.exe"), _testDirectory);
        return new JsonAppSessionStore(paths);
    }
}
