using FilesCollector.Core.Reporting;

namespace FilesCollector.App.History;

public sealed record ReportHistoryEntry(
    string Name,
    string ReportPath,
    string? ManifestPath,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    int? FullCount,
    int? SignaturesCount,
    int? ListedCount,
    int? ExcludedCount,
    int? DiagnosticsCount)
{
    public string CreatedAtText => CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string SizeText => SizeFormatter.Format(SizeBytes);

    public string? CountsText => FullCount is not null
        ? $"{FullCount:N0} full · {SignaturesCount:N0} sig · {ListedCount:N0} listed · {ExcludedCount:N0} excl"
        : null;
}

public sealed record DiagnosticGroup(string Code, string Description, int Count, IReadOnlyList<ReportDiagnostic> Items);
