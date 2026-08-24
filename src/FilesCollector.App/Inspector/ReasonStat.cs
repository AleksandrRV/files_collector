namespace FilesCollector.App.Inspector;

public sealed record ReasonStat(string Code, string Description, int Count);

public sealed record ExtensionStat(string Extension, int Count, long SizeBytes, double SizeShare);

public sealed record ProblemFile(string RelativePath, string Reason, string ReasonText);
