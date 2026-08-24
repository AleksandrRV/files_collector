namespace FilesCollector.Core.Reporting;

public sealed record ReportGenerationProgress(int CompletedFiles, int TotalFiles, string CurrentPath);
