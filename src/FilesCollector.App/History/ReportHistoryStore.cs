using System.Text.Json;
using System.Text.Json.Serialization;
using FilesCollector.Core.Reporting;

namespace FilesCollector.App.History;

public interface IReportHistoryStore
{
    IReadOnlyList<ReportHistoryEntry> GetEntries(string outputsDirectory);

    IReadOnlyList<DiagnosticGroup> GetDiagnosticsGroups(string manifestPath);
}

public sealed class ReportHistoryStore : IReportHistoryStore
{
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public IReadOnlyList<ReportHistoryEntry> GetEntries(string outputsDirectory)
    {
        var entries = new List<ReportHistoryEntry>();
        try
        {
            if (!Directory.Exists(outputsDirectory))
            {
                return entries;
            }

            foreach (var reportPath in Directory.EnumerateFiles(outputsDirectory, "*.md"))
            {
                try
                {
                    var fileInfo = new FileInfo(reportPath);
                    var manifestPath = Path.ChangeExtension(reportPath, ".manifest.json");
                    var manifest = File.Exists(manifestPath) ? TryLoadManifest(manifestPath) : null;

                    var fileTimeUtc = (File.Exists(manifestPath) ? File.GetLastWriteTimeUtc(manifestPath) : fileInfo.LastWriteTimeUtc).ToUniversalTime();
                    var createdAt = manifest?.CreatedAt ?? new DateTimeOffset(fileTimeUtc, TimeSpan.Zero);

                    entries.Add(new ReportHistoryEntry(
                        Path.GetFileNameWithoutExtension(reportPath),
                        reportPath,
                        manifest is not null ? manifestPath : null,
                        createdAt,
                        fileInfo.Length,
                        manifest?.FullCount,
                        manifest?.SignaturesCount,
                        manifest?.ListedCount,
                        manifest?.ExcludedCount,
                        manifest?.Diagnostics.Count));
                }
                catch (IOException)
                {
                    // Skip unreadable entries.
                }
            }
        }
        catch (IOException)
        {
            return entries;
        }

        return entries
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<DiagnosticGroup> GetDiagnosticsGroups(string manifestPath)
    {
        var manifest = TryLoadManifest(manifestPath);
        if (manifest is null)
        {
            return [];
        }

        return manifest.Diagnostics
            .GroupBy(diagnostic => diagnostic.Code)
            .Select(group => new DiagnosticGroup(
                group.Key,
                ReasonCatalog.ShortName(group.Key),
                group.Count(),
                group.Take(30).ToList()))
            .OrderByDescending(group => group.Count)
            .ToList();
    }

    private static ReportManifest? TryLoadManifest(string manifestPath)
    {
        try
        {
            return JsonSerializer.Deserialize<ReportManifest>(File.ReadAllText(manifestPath), ManifestOptions);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
