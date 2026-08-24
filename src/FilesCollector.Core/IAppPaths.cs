namespace FilesCollector.Core;

public interface IAppPaths
{
    string ExecutablePath { get; }
    string ApplicationDirectory { get; }
    string DocumentationDirectory { get; }
    string OutputsDirectory { get; }
    string LocalDataDirectory { get; }
    bool IsPortableMode { get; }
    string PresetsDirectory { get; }
    string PrefixesDirectory { get; }
    string CacheDirectory { get; }
    string LogsDirectory { get; }
}
