using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Planning;

public static class GlobMatcher
{
    // A pattern without a slash is matched against both the full relative path and the
    // bare file name, so "*.cs" selects C# files in every directory. A pattern that
    // contains a slash is anchored to the full relative path only ("src/*.cs" matches
    // just the direct children of src).
    public static bool IsMatch(string relativePath, IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(patterns);

        var normalizedPath = RuleSet.NormalizeRelativePath(relativePath);
        var separatorIndex = normalizedPath.LastIndexOf('/');
        var fileName = separatorIndex >= 0 ? normalizedPath[(separatorIndex + 1)..] : normalizedPath;
        return patterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Any(pattern => MatchesValue(normalizedPath, pattern) || MatchesValue(fileName, pattern));
    }

    private static bool MatchesValue(string value, string pattern)
    {
        return Regex.IsMatch(value, ToRegexPattern(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
