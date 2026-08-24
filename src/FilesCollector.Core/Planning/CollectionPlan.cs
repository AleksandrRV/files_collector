using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Planning;

public sealed class CollectionPlan
{
    public CollectionPlan(IReadOnlyList<CollectionPlanItem> items, IReadOnlyDictionary<string, int> extensionCounts)
    {
        Items = items;
        ExtensionCounts = extensionCounts;
    }

    public IReadOnlyList<CollectionPlanItem> Items { get; }

    public IReadOnlyDictionary<string, int> ExtensionCounts { get; }

    public int FullCount => Items.Count(item => item.Mode == CollectionMode.Full);

    public int SignaturesCount => Items.Count(item => item.Mode == CollectionMode.Signatures);

    public int ListedCount => Items.Count(item => item.Mode == CollectionMode.Listed);

    public int ExcludedCount => Items.Count(item => item.Mode == CollectionMode.Excluded);

    public long EstimatedBytes => Items
        .Where(item => item.Mode is CollectionMode.Full or CollectionMode.Signatures)
        .Sum(item => item.SizeBytes ?? 0);
}
