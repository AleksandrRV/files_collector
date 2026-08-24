using FilesCollector.Core.Planning;

namespace FilesCollector.Core.Reporting;

public sealed record ReportGenerationRequest(
    string RootPath,
    string PresetName,
    string PrefixPresetName,
    string PrefixContent,
    bool RedactRootPath,
    bool IncludeFileMetadataBlocks,
    CollectionPlan Plan);
