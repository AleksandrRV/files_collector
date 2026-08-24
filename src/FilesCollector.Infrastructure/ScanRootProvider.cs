using FilesCollector.Core;

namespace FilesCollector.Infrastructure;

public sealed class ScanRootProvider : IScanRootProvider
{
    private readonly IAppPaths _appPaths;

    public ScanRootProvider(IAppPaths appPaths)
    {
        _appPaths = appPaths;
    }

    public string GetDefaultRoot()
    {
        return Directory.GetParent(_appPaths.ApplicationDirectory)?.FullName
            ?? _appPaths.ApplicationDirectory;
    }

    public bool IsApplicationDirectoryInsideRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var normalizedRoot = AppendDirectorySeparator(Path.GetFullPath(rootPath));
        var normalizedApplicationDirectory = AppendDirectorySeparator(Path.GetFullPath(_appPaths.ApplicationDirectory));
        return normalizedApplicationDirectory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string AppendDirectorySeparator(string path)
    {
        return Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
    }
}
