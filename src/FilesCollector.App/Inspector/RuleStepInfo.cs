using FilesCollector.Core.Rules;

namespace FilesCollector.App.Inspector;

public sealed record RuleStepInfo(string Label, string? Detail, CollectionMode? Mode, bool IsWinner)
{
    public string ModeText => Mode?.ToString() ?? "—";
}
