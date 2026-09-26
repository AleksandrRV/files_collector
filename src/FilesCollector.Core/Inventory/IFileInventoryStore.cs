namespace FilesCollector.Core.Inventory;

public interface IFileInventoryStore
{
    FileInventorySnapshot? Load(string rootPath);

    /// <summary>
    /// Walks the root and saves the snapshot. With <paramref name="followReparsePoints"/>
    /// directory links are traversed; a link whose target has already been visited is
    /// skipped, so cycles terminate.
    /// </summary>
    FileInventorySnapshot Refresh(string rootPath, string? excludedDirectoryPath, bool followReparsePoints, IProgress<InventoryRefreshProgress>? progress, CancellationToken cancellationToken);
}
