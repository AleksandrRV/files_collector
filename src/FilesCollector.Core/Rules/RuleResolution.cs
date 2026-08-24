namespace FilesCollector.Core.Rules;

public sealed record RuleResolution(CollectionMode Mode, RuleSource Source, PathRule? Rule);
