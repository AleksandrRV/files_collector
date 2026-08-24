namespace FilesCollector.Core.FileSystem;

public interface IFileSystem
{
    DirectoryReadResult GetChildren(string directoryPath, string? excludedDirectoryPath);
}
