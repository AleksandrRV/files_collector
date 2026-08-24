using CommunityToolkit.Mvvm.ComponentModel;
using FilesCollector.App.History;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Rules;

namespace FilesCollector.App.Inspector;

public sealed partial class PlanViewModel : ObservableObject
{
    private const long LargeReportThresholdBytes = 25L * 1024 * 1024;
    private const double TokensPerByte = 1d / 3d;
    private const long ContextWindowTokens = 128_000;

    [ObservableProperty]
    private bool isAvailable;

    [ObservableProperty]
    private int fullCount;

    [ObservableProperty]
    private int signaturesCount;

    [ObservableProperty]
    private int listedCount;

    [ObservableProperty]
    private int excludedCount;

    [ObservableProperty]
    private string fullSizeText = string.Empty;

    [ObservableProperty]
    private string signaturesSizeText = string.Empty;

    [ObservableProperty]
    private string listedSizeText = string.Empty;

    [ObservableProperty]
    private string excludedSizeText = string.Empty;

    [ObservableProperty]
    private long estimatedBytes;

    [ObservableProperty]
    private string estimatedBytesText = string.Empty;

    [ObservableProperty]
    private string tokensText = string.Empty;

    [ObservableProperty]
    private string contextNote = string.Empty;

    [ObservableProperty]
    private bool isLargeWarning;

    [ObservableProperty]
    private double fullShare;

    [ObservableProperty]
    private double signaturesShare;

    [ObservableProperty]
    private double listedShare;

    [ObservableProperty]
    private double excludedShare;

    [ObservableProperty]
    private string lastGenerationLabel = string.Empty;

    [ObservableProperty]
    private bool hasReasons;

    [ObservableProperty]
    private bool hasExtensions;

    [ObservableProperty]
    private bool hasProblemFiles;

    [ObservableProperty]
    private bool hasDiagnostics;

    public ObservableCollection<ReasonStat> TopReasons { get; } = [];

    public ObservableCollection<ExtensionStat> TopExtensions { get; } = [];

    public ObservableCollection<ProblemFile> ProblemFiles { get; } = [];

    public ObservableCollection<DiagnosticGroup> DiagnosticsGroups { get; } = [];

    public void MarkUnavailable()
    {
        IsAvailable = false;
        EstimatedBytesText = string.Empty;
        TokensText = string.Empty;
        ContextNote = string.Empty;
        IsLargeWarning = false;
    }

    public void Refresh(CollectionPlan plan, IReadOnlyDictionary<string, long> extensionSizes, long totalSizeBytes)
    {
        var fullItems = plan.Items.Where(item => item.Mode == CollectionMode.Full).ToList();
        var signatureItems = plan.Items.Where(item => item.Mode == CollectionMode.Signatures).ToList();
        var listedItems = plan.Items.Where(item => item.Mode == CollectionMode.Listed).ToList();
        var excludedItems = plan.Items.Where(item => item.Mode == CollectionMode.Excluded).ToList();

        FullCount = fullItems.Count;
        SignaturesCount = signatureItems.Count;
        ListedCount = listedItems.Count;
        ExcludedCount = excludedItems.Count;

        FullSizeText = SizeFormatter.FormatCompact(fullItems.Sum(item => item.SizeBytes ?? 0));
        SignaturesSizeText = SizeFormatter.FormatCompact(signatureItems.Sum(item => item.SizeBytes ?? 0));
        ListedSizeText = SizeFormatter.FormatCompact(listedItems.Sum(item => item.SizeBytes ?? 0));
        ExcludedSizeText = SizeFormatter.FormatCompact(excludedItems.Sum(item => item.SizeBytes ?? 0));

        EstimatedBytes = plan.EstimatedBytes;
        EstimatedBytesText = SizeFormatter.Format(plan.EstimatedBytes);

        var tokens = (long)Math.Ceiling(plan.EstimatedBytes * TokensPerByte);
        TokensText = tokens > 0 ? $"≈ {tokens / 1000d:0.#}k tokens" : string.Empty;
        ContextNote = tokens <= 0
            ? string.Empty
            : tokens <= ContextWindowTokens
                ? "fits a 128k context"
                : $"exceeds 128k by ≈ {(tokens - ContextWindowTokens) / 1000d:0.#}k tokens";

        IsLargeWarning = plan.EstimatedBytes > LargeReportThresholdBytes;
        IsAvailable = true;

        var total = Math.Max(1, plan.Items.Count);
        FullShare = (double)FullCount / total;
        SignaturesShare = (double)SignaturesCount / total;
        ListedShare = (double)ListedCount / total;
        ExcludedShare = (double)ExcludedCount / total;

        TopReasons.Clear();
        foreach (var group in plan.Items
                     .Where(item => item.Reason is not null)
                     .GroupBy(item => item.Reason!)
                     .OrderByDescending(group => group.Count())
                     .Take(6))
        {
            TopReasons.Add(new ReasonStat(group.Key, ReasonCatalog.ShortName(group.Key), group.Count()));
        }

        TopExtensions.Clear();
        foreach (var (extension, count) in plan.ExtensionCounts
                     .OrderByDescending(pair => pair.Value)
                     .Take(10))
        {
            var size = extensionSizes.GetValueOrDefault(extension, 0);
            TopExtensions.Add(new ExtensionStat(extension, count, size, totalSizeBytes > 0 ? (double)size / totalSizeBytes : 0));
        }

        ProblemFiles.Clear();
        foreach (var item in plan.Items
                     .Where(item => item.Reason is "file_unavailable" or "read_failed" or "decode_failed" or "signature_extraction_failed")
                     .Take(20))
        {
            ProblemFiles.Add(new ProblemFile(item.RelativePath, item.Reason ?? string.Empty, ReasonCatalog.Describe(item.Reason)));
        }

        HasReasons = TopReasons.Count > 0;
        HasExtensions = TopExtensions.Count > 0;
        HasProblemFiles = ProblemFiles.Count > 0;
    }

    public void SetDiagnostics(IReadOnlyList<DiagnosticGroup> groups, string lastGenerationLabel)
    {
        DiagnosticsGroups.Clear();
        foreach (var group in groups)
        {
            DiagnosticsGroups.Add(group);
        }

        LastGenerationLabel = lastGenerationLabel;
        HasDiagnostics = DiagnosticsGroups.Count > 0;
    }
}
