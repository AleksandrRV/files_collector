namespace FilesCollector.Core.Signatures;

public sealed record SignatureExtractionResult(bool IsSuccessful, string? Content, string? ExtractorId, string? ErrorCode);
