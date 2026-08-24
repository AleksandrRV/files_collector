using FilesCollector.Core.Prefixes;
using FilesCollector.Infrastructure;
using FilesCollector.Infrastructure.Prefixes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class JsonPrefixPresetRepositoryTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Saved_prefix_preset_is_restored_after_repository_is_recreated()
    {
        var repository = CreateRepository();
        var now = DateTimeOffset.Now;
        var preset = new PrefixPreset
        {
            Id = Guid.NewGuid(),
            Name = "Code review",
            Content = "Review public API changes.",
            CreatedAt = now,
            UpdatedAt = now
        };

        repository.Save(preset);
        var restored = CreateRepository().Get(preset.Id);

        restored.Should().NotBeNull();
        restored!.Name.Should().Be("Code review");
        restored.Content.Should().Be("Review public API changes.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private JsonPrefixPresetRepository CreateRepository()
    {
        var executablePath = Path.Combine(_testDirectory, "files-collector", "FilesCollector.exe");
        var paths = new AppPaths(executablePath, _testDirectory);
        return new JsonPrefixPresetRepository(paths, NullLogger<JsonPrefixPresetRepository>.Instance);
    }
}
