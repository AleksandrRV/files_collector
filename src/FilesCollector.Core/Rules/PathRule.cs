namespace FilesCollector.Core.Rules;

public sealed record PathRule(string RelativePath, PathRuleKind Kind, CollectionMode Mode);
