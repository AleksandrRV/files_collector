using System.Collections.Concurrent;
using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Ignore;

/// <summary>
/// Evaluates paths against the rules of a single .gitignore file.
/// </summary>
/// <remarks>
/// Matching follows gitignore(5): the last matching pattern decides the result,
/// a negation pattern ("!") re-includes a path, and a path inside an ignored
/// directory stays ignored because Git never descends into it. Patterns are
/// relative to the directory that contains the .gitignore file, so the filter
/// keeps the path of that directory relative to the scan root.
/// </remarks>
public sealed class GitIgnoreFilter
{
    private readonly IReadOnlyList<GitIgnorePattern> _patterns;
    private readonly string _baseRelativePath;
    private readonly ConcurrentDictionary<string, bool> _directoryDecisions = new(StringComparer.OrdinalIgnoreCase);

    private GitIgnoreFilter(string filePath, string baseRelativePath, IReadOnlyList<GitIgnorePattern> patterns)
    {
        FilePath = filePath;
        _baseRelativePath = baseRelativePath;
        _patterns = patterns;
    }

    /// <summary>Absolute path of the .gitignore file the rules were read from.</summary>
    public string FilePath { get; }

    /// <summary>Number of effective patterns, ignoring blank lines and comments.</summary>
    public int PatternCount => _patterns.Count;

    public bool IsEmpty => _patterns.Count == 0;

    /// <summary>
    /// Reads a .gitignore file and builds a filter for the given scan root.
    /// </summary>
    /// <param name="filePath">Absolute or relative path of the .gitignore file.</param>
    /// <param name="rootPath">Scan root the evaluated relative paths belong to.</param>
    public static GitIgnoreFilter Load(string filePath, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var lines = File.ReadAllLines(filePath);
        return Create(filePath, rootPath, lines);
    }

    /// <summary>Builds a filter from already loaded .gitignore lines.</summary>
    public static GitIgnoreFilter Create(string filePath, string rootPath, IEnumerable<string> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(lines);

        var fullPath = Path.GetFullPath(filePath);
        var patterns = lines
            .Select(GitIgnorePattern.TryParse)
            .Where(pattern => pattern is not null)
            .Cast<GitIgnorePattern>()
            .ToArray();

        return new GitIgnoreFilter(fullPath, GetBaseRelativePath(fullPath, rootPath), patterns);
    }

    /// <summary>
    /// Returns <c>true</c> when the entry is ignored by the .gitignore file.
    /// </summary>
    /// <param name="relativePath">Path relative to the scan root; "/" separated.</param>
    /// <param name="isDirectory">Whether the entry is a directory.</param>
    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        if (_patterns.Count == 0)
        {
            return false;
        }

        var normalizedPath = RuleSet.NormalizeRelativePath(relativePath);
        if (normalizedPath.Length == 0)
        {
            return false;
        }

        if (!TryGetPathRelativeToBase(normalizedPath, out var candidatePath))
        {
            return false;
        }

        // Walk the ancestors first: Git never looks inside an ignored directory,
        // so a negation pattern cannot re-include a file below it.
        var separatorIndex = candidatePath.IndexOf('/');
        while (separatorIndex >= 0)
        {
            var directoryPath = candidatePath[..separatorIndex];
            if (IsDirectoryIgnored(directoryPath))
            {
                return true;
            }

            separatorIndex = candidatePath.IndexOf('/', separatorIndex + 1);
        }

        return isDirectory ? IsDirectoryIgnored(candidatePath) : Evaluate(candidatePath, isDirectory: false);
    }

    private bool IsDirectoryIgnored(string directoryPath)
    {
        // Directory decisions are cached because every file lookup re-evaluates all of
        // its ancestors.
        return _directoryDecisions.GetOrAdd(directoryPath, path => Evaluate(path, isDirectory: true));
    }

    private bool Evaluate(string candidatePath, bool isDirectory)
    {
        var isIgnored = false;
        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(candidatePath, isDirectory))
            {
                isIgnored = !pattern.IsNegated;
            }
        }

        return isIgnored;
    }

    private bool TryGetPathRelativeToBase(string normalizedPath, out string candidatePath)
    {
        if (_baseRelativePath.Length == 0)
        {
            candidatePath = normalizedPath;
            return true;
        }

        if (normalizedPath.StartsWith(_baseRelativePath + "/", StringComparison.OrdinalIgnoreCase))
        {
            candidatePath = normalizedPath[(_baseRelativePath.Length + 1)..];
            return true;
        }

        // The entry is outside the directory the .gitignore file applies to.
        candidatePath = string.Empty;
        return false;
    }

    private static string GetBaseRelativePath(string gitIgnoreFullPath, string rootPath)
    {
        var directory = Path.GetDirectoryName(gitIgnoreFullPath);
        if (string.IsNullOrEmpty(directory))
        {
            return string.Empty;
        }

        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(Path.GetFullPath(rootPath), directory);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }

        // A .gitignore stored outside the scan root (or on another drive) cannot be
        // anchored to a subdirectory, so its patterns apply from the root.
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            return string.Empty;
        }

        return RuleSet.NormalizeRelativePath(relativePath);
    }
}
