namespace FilesCollector.App.Controls;

public sealed record SizeBucket(int Index, long Count, double HeightRatio, bool IsAboveThreshold);
