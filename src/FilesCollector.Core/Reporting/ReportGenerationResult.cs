using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Reporting;

public sealed record ReportGenerationResult(
    string ReportPath,
    string ManifestPath,
    int FullCount,
    int SignaturesCount,
    int ListedCount,
    int ExcludedCount,
    IReadOnlyList<ReportFileResult> Files);

public sealed record ReportFileResult(
    string RelativePath,
    CollectionMode Mode,
    long? SizeBytes,
    string? Reason);
