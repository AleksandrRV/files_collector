namespace FilesCollector.Core.FileSystem;

public sealed record DirectoryReadResult(
    IReadOnlyList<FileSystemEntry> Entries,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool IsSuccessful => ErrorCode is null;
}
