namespace FilesCollector.Core;

public interface IScanRootProvider
{
    string GetDefaultRoot();

    bool IsApplicationDirectoryInsideRoot(string rootPath);
}
