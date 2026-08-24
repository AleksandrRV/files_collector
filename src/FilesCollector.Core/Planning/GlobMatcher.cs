using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Planning;

public static class GlobMatcher
{
    public static bool IsMatch(string relativePath, IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(patterns);

        var normalizedPath = RuleSet.NormalizeRelativePath(relativePath);
        return patterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Any(pattern => Regex.IsMatch(normalizedPath, ToRegexPattern(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    private static string ToRegexPattern(string pattern)
    {
        var normalizedPattern = RuleSet.NormalizeRelativePath(pattern);
        var escapedPattern = Regex.Escape(normalizedPattern);
        escapedPattern = escapedPattern.Replace("\\*\\*/", "(?:.*/)?", StringComparison.Ordinal);
        escapedPattern = escapedPattern.Replace("\\*\\*", ".*", StringComparison.Ordinal);
        escapedPattern = escapedPattern.Replace("\\*", "[^/]*", StringComparison.Ordinal);
        escapedPattern = escapedPattern.Replace("\\?", "[^/]", StringComparison.Ordinal);
        return "^" + escapedPattern + "$";
    }
}
