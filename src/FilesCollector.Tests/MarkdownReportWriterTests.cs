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

public sealed class MarkdownReportWriterTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Writer_creates_a_structured_report_with_full_and_listed_files()
    {
        var sourceDirectory = Path.Combine(_testDirectory, "source");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "Program.cs");
        File.WriteAllText(sourcePath, "public class Program { }\n");
        var binaryPath = Path.Combine(sourceDirectory, "asset.bin");
        File.WriteAllBytes(binaryPath, [0, 1, 2]);
        var plan = new CollectionPlan(
        [
            new CollectionPlanItem(sourcePath, "Program.cs", CollectionMode.Full, new FileInfo(sourcePath).Length, null),
            new CollectionPlanItem(binaryPath, "asset.bin", CollectionMode.Full, new FileInfo(binaryPath).Length, null),
            new CollectionPlanItem("ignored.txt", "ignored.txt", CollectionMode.Excluded, 0, "excluded_pattern")
        ],
        new Dictionary<string, int>());
        var writer = new MarkdownReportWriter(
            new AppPaths(Path.Combine(_testDirectory, "files-collector", "FilesCollector.exe"), _testDirectory),
            new TestClock(new DateTimeOffset(2026, 8, 24, 9, 30, 5, TimeSpan.Zero)),
            Array.Empty<ISignatureExtractor>());

        var result = writer.Write(new ReportGenerationRequest(sourceDirectory, "Backend review", "Code review", "Review public APIs.", false, true, plan), null, CancellationToken.None);
        var report = File.ReadAllText(result.ReportPath);

        File.Exists(result.ReportPath).Should().BeTrue();
        File.Exists(result.ManifestPath).Should().BeTrue();
        Path.GetFileName(result.ReportPath).Should().Be("2026.08.24_09.30.05_Backend_review.md");
        report.Should().Contain("Review public APIs.");
        report.Should().Contain("### FILE 1 — `Program.cs`");
        report.Should().Contain("public class Program");
        report.Should().Contain("reason: binary_file");
        report.Should().NotContain("ignored.txt");
        result.FullCount.Should().Be(1);
        result.ListedCount.Should().Be(1);
        result.ExcludedCount.Should().Be(1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; }
    }
}
