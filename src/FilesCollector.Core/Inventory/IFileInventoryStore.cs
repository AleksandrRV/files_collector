namespace FilesCollector.Core.Inventory;

public interface IFileInventoryStore
{
    FileInventorySnapshot? Load(string rootPath);

    FileInventorySnapshot Refresh(string rootPath, string? excludedDirectoryPath, IProgress<InventoryRefreshProgress>? progress, CancellationToken cancellationToken);
}
