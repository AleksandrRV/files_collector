using System.Security.Cryptography;
using System.Text.Json;
using FilesCollector.Core;
using FilesCollector.Core.FileSystem;
using FilesCollector.Core.Inventory;

namespace FilesCollector.Infrastructure.Inventory;

public sealed class JsonFileInventoryStore : IFileInventoryStore
{
    private readonly IAppPaths _appPaths;
    private readonly IFileSystem _fileSystem;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false
    };

    public JsonFileInventoryStore(IAppPaths appPaths, IFileSystem fileSystem)
    {
        _appPaths = appPaths;
        _fileSystem = fileSystem;
    }

    public FileInventorySnapshot? Load(string rootPath)
    {
        try
        {
            var path = GetCachePath(rootPath);
            if (!File.Exists(path))
            {
                return null;
            }

            var snapshot = JsonSerializer.Deserialize<FileInventorySnapshot>(File.ReadAllText(path), _jsonOptions);
            if (snapshot is null || snapshot.SchemaVersion != 1 || !string.Equals(Path.GetFullPath(snapshot.RootPath), Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            snapshot.Files ??= [];
            snapshot.ExtensionCounts ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            return snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public FileInventorySnapshot Refresh(string rootPath, string? excludedDirectoryPath, IProgress<InventoryRefreshProgress>? progress, CancellationToken cancellationToken)
    {
        var normalizedRoot = Path.GetFullPath(rootPath);
        var snapshot = new FileInventorySnapshot
        {
            RootPath = normalizedRoot,
            CreatedAt = DateTimeOffset.Now
        };
        var directories = 0;
        VisitDirectory(normalizedRoot, normalizedRoot, excludedDirectoryPath, snapshot, ref directories, progress, cancellationToken);
        Write(snapshot);
        return snapshot;
    }

    private void VisitDirectory(
        string rootPath,
        string directoryPath,
        string? excludedDirectoryPath,
        FileInventorySnapshot snapshot,
        ref int directories,
        IProgress<InventoryRefreshProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        directories++;
        var result = _fileSystem.GetChildren(directoryPath, excludedDirectoryPath);
        foreach (var entry in result.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Kind == EntryKind.File)
            {
                var relativePath = Path.GetRelativePath(rootPath, entry.FullPath).Replace('\\', '/');
                var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (string.IsNullOrEmpty(extension))
                {
                    extension = "[no extension]";
                }

                snapshot.Files.Add(new FileInventoryEntry(
                    entry.FullPath,
                    relativePath,
                    extension,
                    entry.SizeBytes,
                    entry.IsHidden,
                    entry.IsSystem,
                    entry.IsAccessible,
                    entry.AccessError));
                snapshot.ExtensionCounts[extension] = snapshot.ExtensionCounts.TryGetValue(extension, out var count) ? count + 1 : 1;
                if (snapshot.Files.Count % 200 == 0)
                {
                    progress?.Report(new InventoryRefreshProgress(snapshot.Files.Count, directories, relativePath));
                }
            }
            else if (entry.IsAccessible && !entry.IsReparsePoint)
            {
                VisitDirectory(rootPath, entry.FullPath, excludedDirectoryPath, snapshot, ref directories, progress, cancellationToken);
            }
        }
    }

    private void Write(FileInventorySnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(_appPaths.CacheDirectory);
            var path = GetCachePath(snapshot.RootPath);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, _jsonOptions), new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string GetCachePath(string rootPath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(rootPath)))).ToLowerInvariant();
        return Path.Combine(_appPaths.CacheDirectory, $"inventory-{hash}.json");
    }
}
