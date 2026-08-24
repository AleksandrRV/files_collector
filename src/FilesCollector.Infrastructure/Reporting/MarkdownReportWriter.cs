using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FilesCollector.Core;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Reporting;
using FilesCollector.Core.Rules;
using FilesCollector.Core.Signatures;

namespace FilesCollector.Infrastructure.Reporting;

public sealed class MarkdownReportWriter : IReportWriter
{
    private readonly IAppPaths _appPaths;
    private readonly IClock _clock;
    private readonly IReadOnlyList<ISignatureExtractor> _signatureExtractors;

    public MarkdownReportWriter(IAppPaths appPaths, IClock clock, IEnumerable<ISignatureExtractor> signatureExtractors)
    {
        _appPaths = appPaths;
        _clock = clock;
        _signatureExtractors = signatureExtractors.ToArray();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        CleanupTemporaryFiles();
    }

    public ReportGenerationResult Write(ReportGenerationRequest request, IProgress<ReportGenerationProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Directory.CreateDirectory(_appPaths.OutputsDirectory);

        var outputPath = CreateOutputPath(request.PresetName);
        var manifestPath = Path.ChangeExtension(outputPath, ".manifest.json");
        var temporaryPath = outputPath + ".tmp";
        var temporaryManifestPath = manifestPath + ".tmp";
        var includedItems = request.Plan.Items.Where(item => item.Mode != CollectionMode.Excluded).ToArray();
        var renderedFiles = new List<RenderedFile>(includedItems.Length);

        try
        {
            for (var index = 0; index < includedItems.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = includedItems[index];
                renderedFiles.Add(RenderFile(item));
                progress?.Report(new ReportGenerationProgress(index + 1, includedItems.Length, item.RelativePath));
            }

            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                WriteReport(writer, request, renderedFiles);
            }

            File.Move(temporaryPath, outputPath, true);
            var reportHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outputPath))).ToLowerInvariant();
            WriteManifest(temporaryManifestPath, manifestPath, request, outputPath, reportHash, renderedFiles);
            var results = renderedFiles
                .Select(file => new ReportFileResult(file.Item.RelativePath, file.Mode, file.ActualSizeBytes, file.Reason))
                .ToArray();
            return new ReportGenerationResult(
                outputPath,
                manifestPath,
                results.Count(file => file.Mode == CollectionMode.Full),
                results.Count(file => file.Mode == CollectionMode.Signatures),
                results.Count(file => file.Mode == CollectionMode.Listed),
                request.Plan.ExcludedCount,
                results);
        }
        catch
        {
            DeleteIfExists(temporaryPath);
            DeleteIfExists(temporaryManifestPath);
            DeleteIfExists(outputPath);
            throw;
        }
    }

    private RenderedFile RenderFile(CollectionPlanItem item)
    {
        if (item.Mode == CollectionMode.Listed)
        {
            return new RenderedFile(item, CollectionMode.Listed, null, null, item.Reason ?? "content_omitted");
        }

        try
        {
            var bytes = File.ReadAllBytes(item.FullPath);
            if (IsBinary(bytes))
            {
                return new RenderedFile(item, CollectionMode.Listed, null, null, "binary_file");
            }

            var decoded = DecodeText(bytes);
            var normalizedContent = NormalizeLineEndings(decoded.Content);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (item.Mode == CollectionMode.Signatures)
            {
                var extractor = _signatureExtractors.FirstOrDefault(candidate => candidate.CanHandle(Path.GetExtension(item.RelativePath)));
                if (extractor is null)
                {
                    return new RenderedFile(item, CollectionMode.Listed, null, null, "signature_extractor_unavailable", bytes.LongLength, hash);
                }

                var extraction = extractor.Extract(item.FullPath, normalizedContent);
                if (!extraction.IsSuccessful || string.IsNullOrWhiteSpace(extraction.Content))
                {
                    return new RenderedFile(item, CollectionMode.Listed, null, null, extraction.ErrorCode ?? "signature_extraction_failed", bytes.LongLength, hash, extraction.ExtractorId);
                }

                return new RenderedFile(item, CollectionMode.Signatures, extraction.Content, decoded.EncodingName, extraction.ErrorCode, bytes.LongLength, hash, extraction.ExtractorId);
            }

            return new RenderedFile(item, CollectionMode.Full, normalizedContent, decoded.EncodingName, null, bytes.LongLength, hash);
        }
        catch (UnauthorizedAccessException)
        {
            return new RenderedFile(item, CollectionMode.Listed, null, null, "access_denied");
        }
        catch (IOException)
        {
            return new RenderedFile(item, CollectionMode.Listed, null, null, "read_failed");
        }
        catch (DecoderFallbackException)
        {
            return new RenderedFile(item, CollectionMode.Listed, null, null, "decode_failed");
        }
    }

    private void WriteReport(StreamWriter writer, ReportGenerationRequest request, IReadOnlyList<RenderedFile> files)
    {
        writer.WriteLine("<!-- files-collector-report: 1 -->");
        writer.WriteLine("# Files Collector report");
        writer.WriteLine();

        if (!string.IsNullOrWhiteSpace(request.PrefixContent))
        {
            writer.WriteLine(request.PrefixContent.TrimEnd());
            writer.WriteLine();
        }

        writer.WriteLine("## Report metadata");
        writer.WriteLine();
        writer.WriteLine("```yaml");
        writer.WriteLine($"created_at: {_clock.Now:O}");
        writer.WriteLine($"root: {EscapeYaml(request.RedactRootPath ? "<redacted>" : request.RootPath)}");
        writer.WriteLine($"preset: {EscapeYaml(request.PresetName)}");
        writer.WriteLine($"prefix_preset: {EscapeYaml(request.PrefixPresetName)}");
        writer.WriteLine($"included:");
        writer.WriteLine($"  full: {files.Count(file => file.Mode == CollectionMode.Full)}");
        writer.WriteLine($"  signatures: {files.Count(file => file.Mode == CollectionMode.Signatures)}");
        writer.WriteLine($"  listed: {files.Count(file => file.Mode == CollectionMode.Listed)}");
        writer.WriteLine($"excluded: {request.Plan.ExcludedCount}");
        writer.WriteLine("```");
        writer.WriteLine();
        writer.WriteLine("## File index");
        writer.WriteLine();
        writer.WriteLine("| # | Path | Mode | Bytes | Status |");
        writer.WriteLine("|--:|---|---|---:|---|");
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            writer.WriteLine($"| {index + 1} | `{EscapeInlineCode(file.Item.RelativePath)}` | {file.Mode} | {file.Item.SizeBytes ?? 0} | {file.Reason ?? "read"} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Files");
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            writer.WriteLine();
            writer.WriteLine($"### FILE {index + 1} — `{EscapeInlineCode(file.Item.RelativePath)}`");
            if (request.IncludeFileMetadataBlocks)
            {
                writer.WriteLine();
                writer.WriteLine("```yaml");
                writer.WriteLine($"path: {EscapeYaml(file.Item.RelativePath)}");
                writer.WriteLine($"mode: {file.Mode}");
                writer.WriteLine($"bytes: {file.Item.SizeBytes ?? 0}");
                if (file.Mode == CollectionMode.Listed)
                {
                    writer.WriteLine("content: omitted");
                    writer.WriteLine($"reason: {file.Reason}");
                }
                else
                {
                    writer.WriteLine($"encoding: {file.EncodingName}");
                    writer.WriteLine($"sha256: {file.Hash}");
                    if (file.ExtractorId is not null)
                    {
                        writer.WriteLine($"extractor: {file.ExtractorId}");
                    }
                }

                writer.WriteLine("```");
            }
            if ((file.Mode is CollectionMode.Full or CollectionMode.Signatures) && file.Content is not null)
            {
                writer.WriteLine();
                var fence = GetCodeFence(file.Content);
                writer.WriteLine($"{fence}{GetLanguage(file.Item.RelativePath)}");
                writer.Write(file.Content);
                if (!file.Content.EndsWith('\n'))
                {
                    writer.WriteLine();
                }

                writer.WriteLine(fence);
            }
        }
    }

    private void WriteManifest(
        string temporaryManifestPath,
        string manifestPath,
        ReportGenerationRequest request,
        string reportPath,
        string reportHash,
        IReadOnlyList<RenderedFile> renderedFiles)
    {
        var renderedByPath = renderedFiles.ToDictionary(file => file.Item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var manifest = new ReportManifest
        {
            CreatedAt = _clock.Now,
            Root = request.RedactRootPath ? "<redacted>" : request.RootPath,
            PresetName = request.PresetName,
            PrefixPresetName = request.PrefixPresetName,
            ReportFileName = Path.GetFileName(reportPath),
            ReportSha256 = reportHash,
            FullCount = renderedFiles.Count(file => file.Mode == CollectionMode.Full),
            SignaturesCount = renderedFiles.Count(file => file.Mode == CollectionMode.Signatures),
            ListedCount = renderedFiles.Count(file => file.Mode == CollectionMode.Listed),
            ExcludedCount = request.Plan.ExcludedCount
        };

        foreach (var item in request.Plan.Items)
        {
            if (renderedByPath.TryGetValue(item.RelativePath, out var rendered))
            {
                manifest.Files.Add(new ReportManifestFile
                {
                    RelativePath = item.RelativePath,
                    Mode = rendered.Mode,
                    SizeBytes = rendered.ActualSizeBytes ?? item.SizeBytes,
                    Status = rendered.Reason ?? "read",
                    Encoding = rendered.EncodingName,
                    Sha256 = rendered.Hash,
                    Extractor = rendered.ExtractorId,
                    Reason = rendered.Reason
                });
                if (rendered.Reason is not null)
                {
                    manifest.Diagnostics.Add(new ReportDiagnostic(rendered.Reason, item.RelativePath, GetDiagnosticMessage(rendered.Reason)));
                }
            }
            else
            {
                var reason = item.Reason ?? "collection_mode_excluded";
                manifest.Files.Add(new ReportManifestFile
                {
                    RelativePath = item.RelativePath,
                    Mode = CollectionMode.Excluded,
                    SizeBytes = item.SizeBytes,
                    Status = reason,
                    Reason = reason
                });
                manifest.Diagnostics.Add(new ReportDiagnostic(reason, item.RelativePath, GetDiagnosticMessage(reason)));
            }
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(temporaryManifestPath, JsonSerializer.Serialize(manifest, options), new UTF8Encoding(false));
        File.Move(temporaryManifestPath, manifestPath, true);
    }

    private void CleanupTemporaryFiles()
    {
        try
        {
            if (!Directory.Exists(_appPaths.OutputsDirectory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(_appPaths.OutputsDirectory, "*.tmp"))
            {
                DeleteIfExists(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string GetDiagnosticMessage(string code)
    {
        return code switch
        {
            "binary_file" => "The file contains binary data.",
            "read_failed" => "The file could not be read.",
            "access_denied" => "Access to the file was denied.",
            "decode_failed" => "The text encoding could not be decoded.",
            "size_limit" => "The file exceeds the configured size limit.",
            "extension_disabled" => "The file extension is disabled by the active preset.",
            "excluded_pattern" => "The file matches an exclusion pattern.",
            "not_included_pattern" => "The file does not match an inclusion pattern.",
            "hidden_file" => "The file is hidden and hidden files are disabled.",
            "system_file" => "The file is a system file and system files are disabled.",
            "signature_extractor_unavailable" => "No signature extractor is available for this file.",
            _ => "The file was omitted by the active collection policy."
        };
    }

    private string CreateOutputPath(string presetName)
    {
        var safeName = SanitizeFileName(presetName);
        var timestamp = _clock.Now.ToString("yyyy.MM.dd_HH.mm.ss");
        var baseName = $"{timestamp}_{safeName}";
        var path = Path.Combine(_appPaths.OutputsDirectory, baseName + ".md");
        var suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(_appPaths.OutputsDirectory, $"{baseName}_{suffix:D2}.md");
            suffix++;
        }

        return path;
    }

    private static string SanitizeFileName(string name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "Preset" : name.Trim();
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalidCharacters.Contains(character) || char.IsWhiteSpace(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Preset" : sanitized;
    }

    private static bool IsBinary(byte[] bytes)
    {
        if (bytes.Contains((byte)0))
        {
            return true;
        }

        if (bytes.Length == 0)
        {
            return false;
        }

        var controlBytes = bytes.Count(value => value < 9 || value is > 13 and < 32);
        return (double)controlBytes / bytes.Length > 0.1;
    }

    private static DecodedText DecodeText(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return new DecodedText(new UTF8Encoding(false, true).GetString(bytes[3..]), "utf-8-bom");
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return new DecodedText(Encoding.Unicode.GetString(bytes[2..]), "utf-16-le");
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return new DecodedText(Encoding.BigEndianUnicode.GetString(bytes[2..]), "utf-16-be");
        }

        try
        {
            return new DecodedText(new UTF8Encoding(false, true).GetString(bytes), "utf-8");
        }
        catch (DecoderFallbackException)
        {
            return new DecodedText(Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes), "windows-1251");
        }
    }

    private static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static string GetCodeFence(string content)
    {
        var longestRun = 0;
        var currentRun = 0;
        foreach (var character in content)
        {
            if (character == '`')
            {
                currentRun++;
                longestRun = Math.Max(longestRun, currentRun);
            }
            else
            {
                currentRun = 0;
            }
        }

        return new string('`', Math.Max(3, longestRun + 1));
    }

    private static string GetLanguage(string relativePath)
    {
        return Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".json" => "json",
            ".js" or ".mjs" or ".cjs" => "javascript",
            ".ts" or ".tsx" => "typescript",
            ".py" => "python",
            ".md" => "markdown",
            ".html" => "html",
            ".css" => "css",
            ".yaml" or ".yml" => "yaml",
            _ => string.Empty
        };
    }

    private static string EscapeYaml(string value)
    {
        return '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
    }

    private static string EscapeInlineCode(string value)
    {
        return value.Replace("`", "\\`", StringComparison.Ordinal);
    }

    private sealed record RenderedFile(
        CollectionPlanItem Item,
        CollectionMode Mode,
        string? Content,
        string? EncodingName,
        string? Reason,
        long? ActualSizeBytes = null,
        string? Hash = null,
        string? ExtractorId = null);

    private sealed record DecodedText(string Content, string EncodingName);
}
