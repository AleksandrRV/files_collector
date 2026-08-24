using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Reporting;

public sealed class ReportManifest
{
    public int SchemaVersion { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public string Root { get; set; } = string.Empty;

    public string PresetName { get; set; } = string.Empty;

    public string PrefixPresetName { get; set; } = string.Empty;

    public string ReportFileName { get; set; } = string.Empty;

    public string ReportSha256 { get; set; } = string.Empty;

    public int FullCount { get; set; }

    public int SignaturesCount { get; set; }

    public int ListedCount { get; set; }

    public int ExcludedCount { get; set; }

    public List<ReportManifestFile> Files { get; set; } = [];

    public List<ReportDiagnostic> Diagnostics { get; set; } = [];
}

public sealed class ReportManifestFile
{
    public string RelativePath { get; set; } = string.Empty;

    public CollectionMode Mode { get; set; }

    public long? SizeBytes { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Encoding { get; set; }

    public string? Sha256 { get; set; }

    public string? Extractor { get; set; }

    public string? Reason { get; set; }
}
