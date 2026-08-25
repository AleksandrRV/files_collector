namespace FilesCollector.App;

/// <summary>A previously generated report shown in the History tab.</summary>
public sealed record ReportHistoryItem(string FilePath, string DisplayName, string Details);
