namespace FilesCollector.Core.Prefixes;

public interface IPrefixPresetRepository
{
    IReadOnlyList<PrefixPreset> GetAll();

    PrefixPreset? Get(Guid id);

    void Save(PrefixPreset preset);

    bool Delete(Guid id);
}
