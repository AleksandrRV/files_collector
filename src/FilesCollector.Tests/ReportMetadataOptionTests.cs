using FilesCollector.Core;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Reporting;
using FilesCollector.Core.Rules;
using FilesCollector.Core.Signatures;
using FilesCollector.Infrastructure;
using FilesCollector.Infrastructure.Reporting;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class ReportMetadataOptionTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Writer_omits_per_file_yaml_blocks_when_requested()
    {
        var sourceDirectory = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "Program.cs");
        File.WriteAllText(sourcePath, "public class Program { }\n");
        var plan = new CollectionPlan([new CollectionPlanItem(sourcePath, "Program.cs", CollectionMode.Full, 25, null)], new Dictionary<string, int>());
        var writer = new MarkdownReportWriter(
            new AppPaths(Path.Combine(_testDirectory, "files-collector", "FilesCollector.exe"), _testDirectory),
            new FixedClock(),
            Array.Empty<ISignatureExtractor>());

        var result = writer.Write(new ReportGenerationRequest(sourceDirectory, "Default", "No prefix", string.Empty, false, false, plan), null, CancellationToken.None);
        var report = File.ReadAllText(result.ReportPath);

        report.Should().Contain("### FILE 1 — `Program.cs`");
        report.Should().NotContain("path: \"Program.cs\"");
        report.Should().Contain("public class Program");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset Now => new(2026, 8, 24, 9, 30, 5, TimeSpan.Zero);
    }
}
