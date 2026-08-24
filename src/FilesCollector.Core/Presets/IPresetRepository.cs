namespace FilesCollector.Core.Presets;

public interface IPresetRepository
{
    IReadOnlyList<Preset> GetAll();

    Preset? Get(Guid id);

    void Save(Preset preset);

    bool Delete(Guid id);
}
