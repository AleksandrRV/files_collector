using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FilesCollector.Core.Inventory;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Rules;

namespace FilesCollector.App;

/// <summary>
/// Computes per-pattern match counts over an inventory snapshot with a compiled-regex cache.
/// Uses the same glob syntax as <see cref="GlobMatcher"/>.
/// </summary>
public static class GlobStats
{
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.OrdinalIgnoreCase);

    public static int CountMatches(FileInventorySnapshot inventory, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return 0;
        }

        var regex = RegexCache.GetOrAdd(pattern, static p => new Regex(ToRegexPattern(p), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        var count = 0;
        foreach (var file in inventory.Files)
        {
            if (regex.IsMatch(file.RelativePath.Replace('\\', '/').Trim('/')))
            {
                count++;
            }
        }

        return count;
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
