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

    /// <summary>
    /// Removes every rule located strictly inside the directory (the directory's own
    /// rule is kept), so all inner files and folders fall back to the inherited mode.
    /// </summary>
    public int RemoveDescendantRules(string directoryRelativePath)
    {
        var normalizedPath = NormalizeRelativePath(directoryRelativePath);
        return _rules.RemoveAll(rule => IsStrictDescendantOf(rule.RelativePath, normalizedPath));
    }

    private static bool IsStrictDescendantOf(string candidatePath, string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
        {
            // The root directory: everything except the root itself is a descendant.
            return !string.IsNullOrEmpty(candidatePath);
        }

        return candidatePath.StartsWith(directoryPath + "/", StringComparison.OrdinalIgnoreCase);
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
            .Select((rule, position) => (Rule: rule, Position: position))
            .Where(pair => pair.Rule.Kind == PathRuleKind.Directory && IsSameOrDescendant(normalizedPath, pair.Rule.RelativePath))
            .OrderByDescending(pair => pair.Rule.RelativePath.Length)
            .ThenByDescending(pair => pair.Position)
            .Select(pair => pair.Rule)
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
