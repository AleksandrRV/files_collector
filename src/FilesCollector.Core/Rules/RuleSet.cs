namespace FilesCollector.Core.Rules;

public sealed class RuleSet
{
    private readonly List<PathRule> _rules = [];

    public IReadOnlyList<PathRule> Rules => _rules;

    public void Clear()
    {
        _rules.Clear();
    }

    public void LoadRules(IEnumerable<PathRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _rules.Clear();
        foreach (var rule in rules)
        {
            SetRule(rule.RelativePath, rule.Kind, rule.Mode);
        }
    }

    public void SetRule(string relativePath, PathRuleKind kind, CollectionMode mode)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        _rules.RemoveAll(rule => rule.Kind == kind && string.Equals(rule.RelativePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        _rules.Add(new PathRule(normalizedPath, kind, mode));
    }

    public bool RemoveRule(string relativePath, PathRuleKind kind)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        return _rules.RemoveAll(rule => rule.Kind == kind && string.Equals(rule.RelativePath, normalizedPath, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    public RuleResolution Resolve(string relativePath, PathRuleKind kind, bool isSystemExcluded = false)
    {
        if (isSystemExcluded)
        {
            return new RuleResolution(CollectionMode.Excluded, RuleSource.System, null);
        }

        var normalizedPath = NormalizeRelativePath(relativePath);
        var localRule = _rules.LastOrDefault(rule =>
            rule.Kind == kind &&
            string.Equals(rule.RelativePath, normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (localRule is not null)
        {
            return new RuleResolution(localRule.Mode, RuleSource.Local, localRule);
        }

        var inheritedRule = _rules
            .Where(rule => rule.Kind == PathRuleKind.Directory && IsSameOrDescendant(normalizedPath, rule.RelativePath))
            .OrderByDescending(rule => rule.RelativePath.Length)
            .ThenByDescending(rule => _rules.IndexOf(rule))
            .FirstOrDefault();

        if (inheritedRule is not null)
        {
            return new RuleResolution(inheritedRule.Mode, RuleSource.Inherited, inheritedRule);
        }

        return new RuleResolution(CollectionMode.Full, RuleSource.Global, null);
    }

    public static string NormalizeRelativePath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var normalizedPath = relativePath.Replace('\\', '/').Trim('/');
        if (normalizedPath == ".")
        {
            return string.Empty;
        }

        return normalizedPath;
    }

    private static bool IsSameOrDescendant(string candidatePath, string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
        {
            return true;
        }

        return string.Equals(candidatePath, directoryPath, StringComparison.OrdinalIgnoreCase) ||
            candidatePath.StartsWith(directoryPath + "/", StringComparison.OrdinalIgnoreCase);
    }
}
