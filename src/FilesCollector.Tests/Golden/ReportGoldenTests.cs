using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests.Golden;

/// <summary>
/// End-to-end regression test for the report format: inventory → plan → Markdown.
/// </summary>
/// <remarks>
/// When the report format changes on purpose, regenerate the golden file by running the
/// tests with the environment variable <c>FILES_COLLECTOR_UPDATE_GOLDEN=1</c> and review
/// the diff of <c>Golden/report.golden.md</c> like any other code change.
/// </remarks>
public sealed class ReportGoldenTests : IDisposable
{
    private const string UpdateVariable = "FILES_COLLECTOR_UPDATE_GOLDEN";
    private readonly ReportGoldenFixture _fixture = new();

    [Fact]
    public void Report_matches_the_golden_file()
    {
        var actual = _fixture.Run();
        var goldenPath = GetGoldenSourcePath();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
        }

        // A checkout with core.autocrlf must not break the comparison; the LF guarantee of
        // the report itself is asserted separately below.
        var expected = File.ReadAllText(goldenPath, Encoding.UTF8).Replace("\r\n", "\n");
        actual.Should().Be(expected);
    }

    [Fact]
    public void Report_uses_LF_line_endings_only()
    {
        var bytes = File.ReadAllBytes(RunAndGetReportPath());

        bytes.Should().NotContain((byte)'\r');
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, "the report is UTF-8 without BOM");
    }

    [Fact]
    public void Version_control_metadata_never_reaches_the_report_or_the_manifest_contents()
    {
        var report = _fixture.Run();
        var manifest = _fixture.ReadManifest();

        report.Should().NotContain(".git/");
        report.Should().NotContain("refs/heads/main");
        report.Should().NotContain("example.invalid");
        report.Should().NotContain("vendor/lib/.git");
        manifest.Should().NotContain(".git/", "the .git directory is not traversed at all");
        _fixture.Inventory!.Files.Should().NotContain(file => file.RelativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase));
        _fixture.Plan!.Items.Should().ContainSingle(item => item.RelativePath == "vendor/lib/.git")
            .Which.Reason.Should().Be("vcs_metadata");
    }

    [Fact]
    public void Files_inside_a_hidden_folder_are_excluded_when_hidden_files_are_disabled()
    {
        var report = _fixture.Run();
        var hiddenFile = ReportGoldenFixture.HiddenDirectoryName + "/token.txt";

        report.Should().NotContain("do-not-collect");
        _fixture.Inventory!.Files.Should().ContainSingle(file => file.RelativePath == hiddenFile)
            .Which.IsHidden.Should().BeTrue("the file inherits the Hidden attribute of its folder");
        _fixture.Plan!.Items.Should().ContainSingle(item => item.RelativePath == hiddenFile)
            .Which.Reason.Should().Be("hidden_file");
    }

    [Fact]
    public void Binary_content_is_listed_and_legacy_text_is_decoded()
    {
        var report = _fixture.Run();
        var files = _fixture.Result!.Files.ToDictionary(file => file.RelativePath);

        files["data/blob.bin"].Reason.Should().Be("binary_file");
        files["data/loose-object"].Reason.Should().Be("binary_file");
        files["assets/logo.png"].Reason.Should().Be("binary_extension");
        report.Should().Contain("Привет, мир!");
        report.Should().Contain("encoding: windows-1251");
        report.Should().Contain("UTF-16 заметка");
        report.Should().Contain("encoding: utf-16-le");
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private string RunAndGetReportPath()
    {
        _fixture.Run();
        return _fixture.Result!.ReportPath;
    }

    private static string GetGoldenSourcePath([CallerFilePath] string sourceFilePath = "")
    {
        // The golden file is read from the source tree (next to this file) so that an
        // update rewrites the tracked file; the copy in the output folder is a fallback
        // for runs where the sources are not available.
        var sourcePath = Path.Combine(Path.GetDirectoryName(sourceFilePath) ?? string.Empty, "report.golden.md");
        return File.Exists(sourcePath)
            ? sourcePath
            : Path.Combine(AppContext.BaseDirectory, "Golden", "report.golden.md");
    }
}
