using FilesCollector.Core.FileSystem;

namespace FilesCollector.Infrastructure.FileSystem;

public sealed class WindowsFileSystem : IFileSystem
{
    public DirectoryReadResult GetChildren(string directoryPath, string? excludedDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        try
        {
            var normalizedDirectoryPath = Path.GetFullPath(directoryPath);
            var entries = new List<FileSystemEntry>();

            foreach (var entryPath in Directory.EnumerateFileSystemEntries(normalizedDirectoryPath))
            {
                if (IsExcluded(entryPath, excludedDirectoryPath))
                {
                    continue;
                }

                entries.Add(CreateEntry(entryPath));
            }

            return new DirectoryReadResult(SortEntries(entries), null, null);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new DirectoryReadResult([], "access_denied", exception.Message);
        }
        catch (DirectoryNotFoundException exception)
        {
            return new DirectoryReadResult([], "directory_not_found", exception.Message);
        }
        catch (IOException exception)
        {
            return new DirectoryReadResult([], "directory_read_failed", exception.Message);
        }
    }

    private static FileSystemEntry CreateEntry(string entryPath)
    {
        try
        {
            var attributes = File.GetAttributes(entryPath);
            var isDirectory = attributes.HasFlag(FileAttributes.Directory);
            long? sizeBytes = isDirectory ? null : new FileInfo(entryPath).Length;
            return new FileSystemEntry(
                entryPath,
                Path.GetFileName(entryPath),
                isDirectory ? EntryKind.Directory : EntryKind.File,
                attributes.HasFlag(FileAttributes.ReparsePoint),
                attributes.HasFlag(FileAttributes.Hidden),
                attributes.HasFlag(FileAttributes.System),
                sizeBytes,
                true,
                null);
        }
        catch (UnauthorizedAccessException exception)
        {
            return CreateInaccessibleEntry(entryPath, exception);
        }
        catch (IOException exception)
        {
            return CreateInaccessibleEntry(entryPath, exception);
        }
    }

    private static FileSystemEntry CreateInaccessibleEntry(string entryPath, Exception exception)
    {
        // The kind cannot be determined reliably for an inaccessible entry; probe the
        // file system without throwing and fall back to File so consumers never attempt
        // directory traversal on an entry that may be a plain file.
        var kind = Directory.Exists(entryPath) ? EntryKind.Directory : EntryKind.File;
        return new FileSystemEntry(
            entryPath,
            Path.GetFileName(entryPath),
            kind,
            false,
            false,
            false,
            null,
            false,
            exception.Message);
    }

    private static IReadOnlyList<FileSystemEntry> SortEntries(IEnumerable<FileSystemEntry> entries)
    {
        return entries
            .OrderBy(entry => entry.Kind == EntryKind.Directory ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsExcluded(string entryPath, string? excludedDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(excludedDirectoryPath))
        {
            return false;
        }

        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(entryPath)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(excludedDirectoryPath)),
            StringComparison.OrdinalIgnoreCase);
    }
}
