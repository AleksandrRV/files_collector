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
            if (snapshot is null || snapshot.SchemaVersion != FileInventorySnapshot.CurrentSchemaVersion || !string.Equals(Path.GetFullPath(snapshot.RootPath), Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase))
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
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Safety net for link chains that the visited-target check cannot see.</summary>
    private const int MaxDirectoryDepth = 256;

    public FileInventorySnapshot Refresh(string rootPath, string? excludedDirectoryPath, bool followReparsePoints, IProgress<InventoryRefreshProgress>? progress, CancellationToken cancellationToken)
    {
        var normalizedRoot = Path.GetFullPath(rootPath);
        var snapshot = new FileInventorySnapshot
        {
            RootPath = normalizedRoot,
            CreatedAt = DateTimeOffset.Now,
            FollowsReparsePoints = followReparsePoints
        };
        var directories = 0;
        var walk = new Walk(normalizedRoot, excludedDirectoryPath, followReparsePoints, snapshot, progress, cancellationToken);
        walk.VisitedTargets.Add(GetCanonicalPath(normalizedRoot));
        VisitDirectory(walk, normalizedRoot, false, false, 0, ref directories);
        Write(snapshot);
        return snapshot;
    }

    /// <summary>
    /// Walks one directory. <paramref name="isInsideHidden"/> and
    /// <paramref name="isInsideSystem"/> carry the attributes of the ancestors: Windows
    /// does not propagate the Hidden/System attributes to the entries inside a directory
    /// (Git for Windows, for example, marks only the <c>.git</c> folder itself as hidden),
    /// so the flags are inherited explicitly. Version-control metadata directories are
    /// never traversed; see <see cref="SystemExclusions"/>.
    /// </summary>
    private void VisitDirectory(Walk walk, string directoryPath, bool isInsideHidden, bool isInsideSystem, int depth, ref int directories)
    {
        walk.CancellationToken.ThrowIfCancellationRequested();
        directories++;
        var result = _fileSystem.GetChildren(directoryPath, walk.ExcludedDirectoryPath);
        foreach (var entry in result.Entries)
        {
            walk.CancellationToken.ThrowIfCancellationRequested();
            var isHidden = isInsideHidden || entry.IsHidden;
            var isSystem = isInsideSystem || entry.IsSystem;
            if (entry.Kind == EntryKind.File)
            {
                var relativePath = Path.GetRelativePath(walk.RootPath, entry.FullPath).Replace('\\', '/');
                var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (string.IsNullOrEmpty(extension))
                {
                    extension = "[no extension]";
                }

                walk.Snapshot.Files.Add(new FileInventoryEntry(
                    entry.FullPath,
                    relativePath,
                    extension,
                    entry.SizeBytes,
                    isHidden,
                    isSystem,
                    entry.IsAccessible,
                    entry.AccessError));
                walk.Snapshot.ExtensionCounts[extension] = walk.Snapshot.ExtensionCounts.TryGetValue(extension, out var count) ? count + 1 : 1;
                if (walk.Snapshot.Files.Count % 200 == 0)
                {
                    walk.Progress?.Report(new InventoryRefreshProgress(walk.Snapshot.Files.Count, directories, relativePath));
                }
            }
            else if (ShouldTraverse(walk, entry, depth))
            {
                VisitDirectory(walk, entry.FullPath, isHidden, isSystem, depth + 1, ref directories);
            }
        }
    }

    private static bool ShouldTraverse(Walk walk, FileSystemEntry entry, int depth)
    {
        if (!entry.IsAccessible || SystemExclusions.IsVcsMetadataName(entry.Name) || depth >= MaxDirectoryDepth)
        {
            return false;
        }

        if (!entry.IsReparsePoint)
        {
            return true;
        }

        // A link is followed only when enabled, and each link target at most once. A
        // junction that points back to one of its ancestors is therefore walked once more
        // at most (its target is then known) and the cycle ends; the depth limit is a
        // further safety net.
        return walk.FollowReparsePoints && walk.VisitedTargets.Add(GetCanonicalPath(entry.FullPath));
    }

    /// <summary>Final target of a link, or the path itself for an ordinary directory.</summary>
    private static string GetCanonicalPath(string directoryPath)
    {
        try
        {
            var target = new DirectoryInfo(directoryPath).ResolveLinkTarget(returnFinalTarget: true);
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(target?.FullName ?? directoryPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        }
    }

    private sealed record Walk(
        string RootPath,
        string? ExcludedDirectoryPath,
        bool FollowReparsePoints,
        FileInventorySnapshot Snapshot,
        IProgress<InventoryRefreshProgress>? Progress,
        CancellationToken CancellationToken)
    {
        public HashSet<string> VisitedTargets { get; } = new(StringComparer.OrdinalIgnoreCase);
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
