using System.Text;

namespace FilesCollector.Core.Ignore;

/// <summary>
/// A single parsed line of a .gitignore file. The implementation follows the
/// gitignore(5) pattern format: comments, negation with "!", directory-only
/// patterns ending with "/", patterns anchored to the .gitignore location when
/// they contain a slash, the "**" wildcard, "*", "?", character classes and
/// backslash escaping.
/// </summary>
public sealed class GitIgnorePattern
{
    private readonly Regex _regex;

    private GitIgnorePattern(string source, Regex regex, bool isNegated, bool matchesDirectoriesOnly)
    {
        Source = source;
        _regex = regex;
        IsNegated = isNegated;
        MatchesDirectoriesOnly = matchesDirectoriesOnly;
    }

    /// <summary>The original line, kept for diagnostics and tooltips.</summary>
    public string Source { get; }

    public bool IsNegated { get; }

    public bool MatchesDirectoriesOnly { get; }

    /// <summary>
    /// Parses a single .gitignore line. Returns <c>null</c> for blank lines and
    /// comments, which carry no matching rule.
    /// </summary>
    public static GitIgnorePattern? TryParse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var source = line.TrimEnd('\r');
        var pattern = TrimUnescapedTrailingWhitespace(source);
        if (pattern.Length == 0 || pattern[0] == '#')
        {
            return null;
        }

        var isNegated = false;
        if (pattern[0] == '!')
        {
            isNegated = true;
            pattern = pattern[1..];
        }
        else if (pattern.StartsWith("\\#", StringComparison.Ordinal) || pattern.StartsWith("\\!", StringComparison.Ordinal))
        {
            pattern = pattern[1..];
        }

        var matchesDirectoriesOnly = false;
        while (pattern.Length > 0 && pattern[^1] == '/' && !IsEscaped(pattern, pattern.Length - 1))
        {
            matchesDirectoriesOnly = true;
            pattern = pattern[..^1];
        }

        if (pattern.Length == 0)
        {
            return null;
        }

        // A slash anywhere but at the end anchors the pattern to the directory
        // that contains the .gitignore file.
        var isAnchored = pattern.Contains('/', StringComparison.Ordinal);
        if (pattern[0] == '/')
        {
            pattern = pattern[1..];
            if (pattern.Length == 0)
            {
                return null;
            }
        }

        Regex regex;
        try
        {
            regex = new Regex(
                ToRegexPattern(pattern, isAnchored),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }
        catch (ArgumentException)
        {
            // A malformed pattern must never break the scan; it is simply skipped.
            return null;
        }

        return new GitIgnorePattern(source, regex, isNegated, matchesDirectoriesOnly);
    }

    public bool IsMatch(string relativePath, bool isDirectory)
    {
        if (MatchesDirectoriesOnly && !isDirectory)
        {
            return false;
        }

        return _regex.IsMatch(relativePath);
    }

    private static string TrimUnescapedTrailingWhitespace(string value)
    {
        var end = value.Length;
        while (end > 0 && (value[end - 1] == ' ' || value[end - 1] == '\t'))
        {
            if (IsEscaped(value, end - 1))
            {
                break;
            }

            end--;
        }

        return value[..end];
    }

    private static bool IsEscaped(string value, int index)
    {
        var backslashes = 0;
        for (var position = index - 1; position >= 0 && value[position] == '\\'; position--)
        {
            backslashes++;
        }

        return backslashes % 2 == 1;
    }

    private static string ToRegexPattern(string pattern, bool isAnchored)
    {
        var builder = new StringBuilder("^");
        if (!isAnchored)
        {
            // A pattern without a slash matches at any depth.
            builder.Append("(?:.*/)?");
        }

        var index = 0;
        while (index < pattern.Length)
        {
            var character = pattern[index];
            switch (character)
            {
                case '\\':
                    if (index + 1 < pattern.Length)
                    {
                        builder.Append(Regex.Escape(pattern[index + 1].ToString()));
                        index += 2;
                    }
                    else
                    {
                        builder.Append("\\\\");
                        index++;
                    }

                    break;

                case '*':
                    index = AppendAsterisks(builder, pattern, index);
                    break;

                case '?':
                    builder.Append("[^/]");
                    index++;
                    break;

                case '[':
                    index = AppendCharacterClass(builder, pattern, index);
                    break;

                default:
                    builder.Append(Regex.Escape(character.ToString()));
                    index++;
                    break;
            }
        }

        builder.Append('$');
        return builder.ToString();
    }

    private static int AppendAsterisks(StringBuilder builder, string pattern, int index)
    {
        var start = index;
        var count = 0;
        while (index < pattern.Length && pattern[index] == '*')
        {
            count++;
            index++;
        }

        var isAtSegmentStart = start == 0 || pattern[start - 1] == '/';
        var isFollowedBySlash = index < pattern.Length && pattern[index] == '/';
        var isAtEnd = index == pattern.Length;

        if (count >= 2 && isAtSegmentStart && isFollowedBySlash)
        {
            // "**/" matches zero or more leading directories.
            builder.Append("(?:.*/)?");
            return index + 1;
        }

        if (count >= 2 && isAtSegmentStart && isAtEnd)
        {
            // A trailing "**" matches everything inside.
            builder.Append(".*");
            return index;
        }

        // Any other asterisk sequence behaves like a single asterisk: it matches
        // inside one path segment only.
        builder.Append("[^/]*");
        return index;
    }

    private static int AppendCharacterClass(StringBuilder builder, string pattern, int index)
    {
        var closingIndex = FindClosingBracket(pattern, index);
        if (closingIndex < 0)
        {
            builder.Append("\\[");
            return index + 1;
        }

        var content = pattern[(index + 1)..closingIndex];
        if (content.StartsWith('!'))
        {
            content = "^" + content[1..];
        }

        builder.Append('[').Append(content).Append(']');
        return closingIndex + 1;
    }

    private static int FindClosingBracket(string pattern, int openingIndex)
    {
        var index = openingIndex + 1;
        if (index < pattern.Length && (pattern[index] == '!' || pattern[index] == '^'))
        {
            index++;
        }

        if (index < pattern.Length && pattern[index] == ']')
        {
            index++;
        }

        for (; index < pattern.Length; index++)
        {
            if (pattern[index] == '\\')
            {
                index++;
                continue;
            }

            if (pattern[index] == ']')
            {
                return index;
            }
        }

        return -1;
    }
}
