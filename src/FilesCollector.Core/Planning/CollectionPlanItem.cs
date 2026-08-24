using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Planning;

public sealed record CollectionPlanItem(
    string FullPath,
    string RelativePath,
    CollectionMode Mode,
    long? SizeBytes,
    string? Reason);
