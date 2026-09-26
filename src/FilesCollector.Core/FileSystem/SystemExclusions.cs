namespace FilesCollector.Core.FileSystem;

/// <summary>
/// Built-in exclusions that no user rule can override.
/// </summary>
/// <remarks>
/// Version-control metadata (<c>.git</c>, <c>.svn</c>, <c>.hg</c>, <c>.bzr</c>) is never
/// project content: it holds compressed objects, hooks, reflogs with author e-mails and
/// remote URLs. Git itself never treats its own <c>.git</c> directory as part of the work
/// tree, so the collector follows the same rule. A path is excluded when any of its
/// segments equals one of these names (this also covers the <c>.git</c> file used by
/// submodules and linked worktrees). Names such as <c>.gitignore</c> or
/// <c>.gitattributes</c> are not affected.
/// </remarks>
public static class SystemExclusions
{
    /// <summary>Reason code stored in the plan and in the report manifest.</summary>
    public const string VcsMetadataReason = "vcs_metadata";

    private static readonly HashSet<string> VcsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".svn",
        ".hg",
        ".bzr"
    };

    /// <summary>Names of the version-control metadata entries that are always excluded.</summary>
    public static IReadOnlyCollection<string> VcsMetadataNames => VcsNames;

    /// <summary>Returns <c>true</c> when the entry name is a version-control metadata entry.</summary>
    public static bool IsVcsMetadataName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return VcsNames.Contains(name);
    }

    /// <summary>
    /// Returns <c>true</c> when the relative path is a version-control metadata entry or
    /// lies inside one. Both "/" and "\" separators are accepted.
    /// </summary>
    public static bool IsVcsMetadataPath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var start = 0;
        for (var index = 0; index <= relativePath.Length; index++)
        {
            if (index < relativePath.Length && relativePath[index] != '/' && relativePath[index] != '\\')
            {
                continue;
            }

            if (index > start && VcsNames.Contains(relativePath[start..index]))
            {
                return true;
            }

            start = index + 1;
        }

        return false;
    }
}
