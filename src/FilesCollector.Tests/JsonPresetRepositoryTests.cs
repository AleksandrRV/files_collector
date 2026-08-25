using FilesCollector.Core.Presets;
using FilesCollector.Core.Rules;
using FilesCollector.Infrastructure;
using FilesCollector.Infrastructure.Presets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class JsonPresetRepositoryTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void First_read_creates_the_default_preset()
    {
        var repository = CreateRepository();

        var presets = repository.GetAll();

        presets.Should().ContainSingle();
        presets[0].Id.Should().Be(PresetDefaults.DefaultPresetId);
        presets[0].Name.Should().Be("Default");
        presets[0].GlobalMode.Should().Be(CollectionMode.Full);
    }

    [Fact]
    public void Saved_preset_is_available_after_repository_is_recreated()
    {
        var repository = CreateRepository();
        var now = DateTimeOffset.Now;
        var preset = new Preset
        {
            Id = Guid.NewGuid(),
            Name = "Backend review",
            CreatedAt = now,
            UpdatedAt = now,
            PathRules = [new PathRule("app/node_modules", PathRuleKind.Directory, CollectionMode.Excluded)]
        };

        repository.Save(preset);
        var recreatedRepository = CreateRepository();
        var restored = recreatedRepository.Get(preset.Id);

        restored.Should().NotBeNull();
        restored!.Name.Should().Be("Backend review");
        restored.PathRules.Should().ContainSingle();
        restored.PathRules[0].RelativePath.Should().Be("app/node_modules");
        restored.PathRules[0].Mode.Should().Be(CollectionMode.Excluded);
    }

    [Fact]
    public void Save_with_a_locked_index_keeps_the_preset_file_and_recovers_on_retry()
    {
        var repository = CreateRepository();
        var now = DateTimeOffset.Now;
        var alphaId = Guid.NewGuid();
        repository.Save(new Preset { Id = alphaId, Name = "Alpha", CreatedAt = now, UpdatedAt = now });

        var executablePath = Path.Combine(_testDirectory, "files-collector", "FilesCollector.exe");
        var presetsDirectory = Path.Combine(new AppPaths(executablePath, _testDirectory).LocalDataDirectory, "presets");
        var indexPath = Path.Combine(presetsDirectory, "index.json");
        var betaId = Guid.NewGuid();
        var beta = new Preset { Id = betaId, Name = "Beta", CreatedAt = now, UpdatedAt = now };

        try
        {
            using (File.Open(indexPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                repository.Save(beta);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Expected when the active lock prevents replacing index.json
            // (on Windows File.Move over a locked target reports access denied).
        }

        // The preset file survives even though its index entry could not be written...
        File.Exists(Path.Combine(presetsDirectory, $"{betaId:N}.json")).Should().BeTrue();

        // ...and saving again once the lock is released restores full consistency:
        // neither Alpha nor Beta disappears from the repository.
        repository.Save(beta);
        CreateRepository().GetAll().Select(preset => preset.Name)
            .Should().Contain(new[] { "Alpha", "Beta" });
    }

    [Fact]
    public void Default_preset_cannot_be_deleted()
    {
        var repository = CreateRepository();

        var deleted = repository.Delete(PresetDefaults.DefaultPresetId);

        deleted.Should().BeFalse();
        repository.Get(PresetDefaults.DefaultPresetId).Should().NotBeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private JsonPresetRepository CreateRepository()
    {
        var executablePath = Path.Combine(_testDirectory, "files-collector", "FilesCollector.exe");
        var paths = new AppPaths(executablePath, _testDirectory);
        return new JsonPresetRepository(paths, NullLogger<JsonPresetRepository>.Instance);
    }
}
