using FilesCollector.Core;

namespace FilesCollector.Infrastructure;

public sealed class AppPaths : IAppPaths
{
    public AppPaths(string executablePath, string? localApplicationDataPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        ExecutablePath = Path.GetFullPath(executablePath);
        ApplicationDirectory = Path.GetDirectoryName(ExecutablePath)
            ?? throw new InvalidOperationException("The executable directory could not be determined.");
        DocumentationDirectory = Path.Combine(ApplicationDirectory, "docs");
        OutputsDirectory = Path.Combine(ApplicationDirectory, "outputs");

        var appDataRoot = localApplicationDataPath ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appDataRoot))
        {
            throw new InvalidOperationException("The local application data directory could not be determined.");
        }

        IsPortableMode = File.Exists(Path.Combine(ApplicationDirectory, "portable.mode"));
        LocalDataDirectory = IsPortableMode
            ? Path.Combine(ApplicationDirectory, "outputs", "app-data")
            : Path.Combine(Path.GetFullPath(appDataRoot), "FilesCollector");
        PresetsDirectory = Path.Combine(LocalDataDirectory, "presets");
        PrefixesDirectory = Path.Combine(LocalDataDirectory, "prefixes");
        CacheDirectory = Path.Combine(LocalDataDirectory, "cache");
        LogsDirectory = Path.Combine(LocalDataDirectory, "logs");
    }

    public string ExecutablePath { get; }

    public string ApplicationDirectory { get; }

    public string DocumentationDirectory { get; }

    public string OutputsDirectory { get; }

    public string LocalDataDirectory { get; }

    public bool IsPortableMode { get; }

    public string PresetsDirectory { get; }

    public string PrefixesDirectory { get; }

    public string CacheDirectory { get; }

    public string LogsDirectory { get; }
}
