namespace FilesCollector.Core.FileSystem;

/// <summary>
/// Classifies file contents as text or binary from a sample of the file, so the plan
/// (statistics, tree, token estimate) agrees with what the report writer will do.
/// </summary>
/// <remarks>
/// The planner only calls <see cref="TryGetCachedIsBinary"/>, which never touches the
/// disk; the caller probes files in the background with <see cref="ProbeIsBinary"/> and
/// rebuilds the plan afterwards. Results are keyed by path and size: a file whose size
/// changes is probed again.
/// </remarks>
public interface IFileContentProbe
{
    /// <summary>
    /// Returns <c>true</c>/<c>false</c> when the file was probed at this size, or
    /// <c>null</c> when it has not been probed yet.
    /// </summary>
    bool? TryGetCachedIsBinary(string fullPath, long? sizeBytes);

    /// <summary>
    /// Reads a sample of the file, caches and returns the result. An unreadable file is
    /// cached as not binary: the report writer then reports the actual read error.
    /// </summary>
    bool ProbeIsBinary(string fullPath, long? sizeBytes);
}
