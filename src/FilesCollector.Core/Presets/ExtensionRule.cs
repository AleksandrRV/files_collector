using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Presets;

public sealed record ExtensionRule(string Extension, bool Enabled, CollectionMode Mode);
