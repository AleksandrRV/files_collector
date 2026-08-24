namespace FilesCollector.Core.Settings;

public interface IAppSessionStore
{
    Guid? GetLastPresetId();

    void SetLastPresetId(Guid presetId);
}
