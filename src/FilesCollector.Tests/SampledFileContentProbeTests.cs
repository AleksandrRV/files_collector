using System.Text;
using FilesCollector.Infrastructure.Reporting;
using FilesCollector.Tests.Golden;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class SampledFileContentProbeTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", Guid.NewGuid().ToString("N"));

    public SampledFileContentProbeTests()
    {
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public void Text_and_binary_files_are_classified_and_cached()
    {
        var text = Write("a.txt", Encoding.UTF8.GetBytes("hello\n"));
        var binary = Write("b.bin", ReportGoldenFixture.PseudoRandomBytes(4096, 5, allowZero: false));
        var probe = new SampledFileContentProbe();

        probe.TryGetCachedIsBinary(text, 6).Should().BeNull("nothing has been probed yet");
        probe.ProbeIsBinary(text, 6).Should().BeFalse();
        probe.ProbeIsBinary(binary, 4096).Should().BeTrue();
        probe.TryGetCachedIsBinary(text, 6).Should().BeFalse();
        probe.TryGetCachedIsBinary(binary, 4096).Should().BeTrue();
        probe.TryGetCachedIsBinary(binary, 4097).Should().BeNull("a changed size invalidates the result");
    }

    [Fact]
    public void A_multibyte_character_cut_by_the_sample_boundary_does_not_make_text_binary()
    {
        // 8191 ASCII bytes followed by "ж" (2 bytes): the sample ends after its first byte.
        var content = new string('a', SampledFileContentProbe.SampleSize - 1) + "жжж\n";
        var path = Write("cyrillic.txt", Encoding.UTF8.GetBytes(content));

        new SampledFileContentProbe().ProbeIsBinary(path, content.Length).Should().BeFalse();
    }

    [Fact]
    public void Truncated_sample_helper_keeps_complete_files_strict()
    {
        byte[] cut = [(byte)'a', 0xD0];

        TextContentDecoder.IsBinarySample(cut, isCompleteFile: false).Should().BeFalse();
        TextContentDecoder.Decode(cut).Kind.Should().Be(TextContentKind.Text, "a lone lead byte decodes as Windows-1251 text");
        TextContentDecoder.IsBinarySample([0, 1, 2], isCompleteFile: false).Should().BeTrue();
    }

    [Fact]
    public void Missing_files_are_cached_as_not_binary()
    {
        var probe = new SampledFileContentProbe();
        var path = Path.Combine(_testDirectory, "missing.txt");

        probe.ProbeIsBinary(path, 10).Should().BeFalse();
        probe.TryGetCachedIsBinary(path, 10).Should().BeFalse("the writer reports the read error; the probe must not retry forever");
    }

    public void Dispose()
    {
        Directory.Delete(_testDirectory, true);
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_testDirectory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
