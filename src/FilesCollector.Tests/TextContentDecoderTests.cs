using System.Text;
using FilesCollector.Infrastructure.Reporting;
using FilesCollector.Tests.Golden;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class TextContentDecoderTests
{
    static TextContentDecoderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Empty_file_is_empty_utf8_text()
    {
        var result = TextContentDecoder.Decode([]);

        result.Kind.Should().Be(TextContentKind.Text);
        result.Content.Should().BeEmpty();
        result.EncodingName.Should().Be("utf-8");
    }

    [Fact]
    public void Valid_utf8_is_text()
    {
        var result = TextContentDecoder.Decode(Encoding.UTF8.GetBytes("public class Привет { }\n"));

        result.Kind.Should().Be(TextContentKind.Text);
        result.Content.Should().Be("public class Привет { }\n");
        result.EncodingName.Should().Be("utf-8");
    }

    [Fact]
    public void Utf8_bom_is_removed()
    {
        var result = TextContentDecoder.Decode([0xEF, 0xBB, 0xBF, (byte)'o', (byte)'k']);

        result.Content.Should().Be("ok");
        result.EncodingName.Should().Be("utf-8-bom");
    }

    [Fact]
    public void Utf16_with_a_bom_is_text_although_it_contains_zero_bytes()
    {
        var little = TextContentDecoder.Decode([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("заметка")]);
        var big = TextContentDecoder.Decode([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("note")]);

        little.Kind.Should().Be(TextContentKind.Text);
        little.Content.Should().Be("заметка");
        little.EncodingName.Should().Be("utf-16-le");
        big.Content.Should().Be("note");
        big.EncodingName.Should().Be("utf-16-be");
    }

    [Fact]
    public void Invalid_content_after_a_utf8_bom_is_a_decode_failure()
    {
        TextContentDecoder.Decode([0xEF, 0xBB, 0xBF, 0xC3, 0x28]).Kind.Should().Be(TextContentKind.DecodeFailed);
    }

    [Fact]
    public void Windows1251_text_is_decoded()
    {
        var bytes = Encoding.GetEncoding(1251).GetBytes("Привет, мир!\r\nЁлка — «ель».\r\n");

        var result = TextContentDecoder.Decode(bytes);

        result.Kind.Should().Be(TextContentKind.Text);
        result.EncodingName.Should().Be("windows-1251");
        result.Content.Should().Be("Привет, мир!\r\nЁлка — «ель».\r\n");
    }

    [Fact]
    public void Nul_byte_means_binary()
    {
        TextContentDecoder.Decode([(byte)'a', 0, (byte)'b']).Kind.Should().Be(TextContentKind.Binary);
    }

    [Fact]
    public void Nul_free_noise_is_binary_for_every_seed()
    {
        // The previous detector accepted such data as Windows-1251 text whenever fewer than
        // 10 % of the bytes happened to be control characters.
        for (uint seed = 1; seed <= 300; seed++)
        {
            var bytes = ReportGoldenFixture.PseudoRandomBytes(64 + (int)(seed % 400), seed, allowZero: false);

            TextContentDecoder.Decode(bytes).Kind.Should().Be(TextContentKind.Binary, $"seed {seed} is noise");
        }
    }

    [Fact]
    public void Zlib_stream_such_as_a_git_loose_object_is_binary()
    {
        using var buffer = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(buffer, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Encoding.ASCII.GetBytes("blob 12\0hello world\n"));
        }

        TextContentDecoder.Decode(buffer.ToArray()).Kind.Should().Be(TextContentKind.Binary);
        TextContentDecoder.Decode([0x78, 0x01, 0xC5, 0xE0, 0xE8, 0xF2]).Kind.Should().Be(TextContentKind.Binary, "the zlib header is checked even when the rest looks like Cyrillic");
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x41 })]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A })]
    [InlineData(new byte[] { 0x1F, 0x8B, 0x08, 0x08 })]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14 })]
    public void Known_binary_signatures_are_binary(byte[] bytes)
    {
        TextContentDecoder.Decode(bytes).Kind.Should().Be(TextContentKind.Binary);
    }

    [Fact]
    public void Text_that_starts_like_a_short_signature_stays_text()
    {
        TextContentDecoder.Decode(Encoding.ASCII.GetBytes("PACKAGE.md describes the packages.\n")).Kind.Should().Be(TextContentKind.Text);
        TextContentDecoder.Decode(Encoding.ASCII.GetBytes("MZ is the DOS header magic.\n")).Kind.Should().Be(TextContentKind.Text);
        TextContentDecoder.Decode(Encoding.ASCII.GetBytes("x^2 + y^2\n")).Kind.Should().Be(TextContentKind.Text);
    }

    [Fact]
    public void Ansi_colour_escapes_in_logs_are_text()
    {
        TextContentDecoder.Decode(Encoding.ASCII.GetBytes("\u001b[32mPASS\u001b[0m build\n")).Kind.Should().Be(TextContentKind.Text);
    }
}
