namespace FilesCollector.Core.Inventory;

public sealed record InventoryRefreshProgress(int DiscoveredFiles, int DiscoveredDirectories, string CurrentPath);
