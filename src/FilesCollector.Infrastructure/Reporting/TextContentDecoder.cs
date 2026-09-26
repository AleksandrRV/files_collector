namespace FilesCollector.Infrastructure.Reporting;

/// <summary>Outcome of <see cref="TextContentDecoder.Decode"/>.</summary>
public enum TextContentKind
{
    /// <summary>The bytes are text; <see cref="TextDecodingResult.Content"/> is set.</summary>
    Text,

    /// <summary>The bytes are binary data and must not be rendered as text.</summary>
    Binary,

    /// <summary>A byte order mark was present but the content does not match it.</summary>
    DecodeFailed
}

public sealed record TextDecodingResult(TextContentKind Kind, string? Content, string? EncodingName)
{
    public static TextDecodingResult Binary { get; } = new(TextContentKind.Binary, null, null);

    public static TextDecodingResult DecodeFailed { get; } = new(TextContentKind.DecodeFailed, null, null);
}

/// <summary>
/// Decides whether file bytes are text and decodes them.
/// </summary>
/// <remarks>
/// <para>Order of the checks:</para>
/// <list type="number">
/// <item>A byte order mark (UTF-8, UTF-16 LE/BE) selects the encoding; UTF-16 is
/// checked before the NUL test because UTF-16 text naturally contains zero bytes.</item>
/// <item>Well-known binary signatures (images, archives, executables, zlib/gzip streams,
/// Git pack/index files) are binary.</item>
/// <item>Any NUL byte means binary.</item>
/// <item>Strictly valid UTF-8 is text unless control characters dominate.</item>
/// <item>Otherwise the only candidate is the legacy Windows-1251 code page. Windows-1251
/// maps almost every byte to a character, so decoding success proves nothing; the bytes
/// are accepted only when they contain practically no C0 control characters. Real
/// legacy text has none, while compressed or random data contains roughly one control
/// byte in ten.</item>
/// </list>
/// </remarks>
public static class TextContentDecoder
{
    /// <summary>Share of control bytes tolerated in otherwise valid UTF-8 text.</summary>
    private const double Utf8ControlRatioLimit = 0.10;

    /// <summary>Share of control bytes tolerated in legacy (Windows-1251) text.</summary>
    private const double LegacyControlRatioLimit = 0.005;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16Le = new(false, false, true);
    private static readonly UnicodeEncoding StrictUtf16Be = new(true, false, true);
    private static readonly Lazy<Encoding> StrictWindows1251 = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    });

    private static readonly byte[][] BinarySignatures =
    [
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], // PNG
        [0xFF, 0xD8, 0xFF], // JPEG
        "GIF87a"u8.ToArray(),
        "GIF89a"u8.ToArray(),
        "%PDF-"u8.ToArray(),
        [0x50, 0x4B, 0x03, 0x04], // ZIP, DOCX, JAR, NuGet
        [0x50, 0x4B, 0x05, 0x06], // empty ZIP
        [0x50, 0x4B, 0x07, 0x08], // spanned ZIP
        [0x1F, 0x8B], // gzip
        [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C], // 7z
        [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07], // RAR
        [0x7F, 0x45, 0x4C, 0x46], // ELF
        [0x28, 0xB5, 0x2F, 0xFD], // zstd
        [0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00], // xz
        [0x42, 0x5A, 0x68], // bzip2 ("BZh"), confirmed below by the legacy check
        "PACK"u8.ToArray(), // Git pack file
        "DIRC"u8.ToArray(), // Git index
        [0xFF, 0x74, 0x4F, 0x63], // Git pack index v2
        [0x52, 0x49, 0x44, 0x58], // Git reverse index ("RIDX")
        [0x00, 0x00, 0x01, 0x00], // ICO
        [0x4D, 0x5A] // PE executable ("MZ"), confirmed below by the NUL check
    ];

    private static readonly HashSet<int> AmbiguousSignatureLengths = [2, 3, 4];

    /// <summary>
    /// Classifies the first bytes of a file. When <paramref name="isCompleteFile"/> is
    /// <c>false</c> the sample may end in the middle of a multi-byte character, so an
    /// incomplete trailing UTF-8 (or UTF-16) sequence is ignored.
    /// </summary>
    public static bool IsBinarySample(ReadOnlySpan<byte> sample, bool isCompleteFile)
    {
        if (!isCompleteFile)
        {
            sample = TrimIncompleteTail(sample);
        }

        return Decode(sample).Kind == TextContentKind.Binary;
    }

    public static TextDecodingResult Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return new TextDecodingResult(TextContentKind.Text, string.Empty, "utf-8");
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return TryDecode(StrictUtf8, bytes[3..], "utf-8-bom");
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return TryDecode(StrictUtf16Le, bytes[2..], "utf-16-le");
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return TryDecode(StrictUtf16Be, bytes[2..], "utf-16-be");
        }

        if (HasUnambiguousBinarySignature(bytes) || bytes.Contains((byte)0))
        {
            return TextDecodingResult.Binary;
        }

        if (TryGetString(StrictUtf8, bytes, out var utf8Text))
        {
            return GetControlRatio(bytes) > Utf8ControlRatioLimit
                ? TextDecodingResult.Binary
                : new TextDecodingResult(TextContentKind.Text, utf8Text, "utf-8");
        }

        // Invalid UTF-8 from here on. Short signatures ("BZh", "MZ", a zlib header) are
        // only trusted now, because a text file may legitimately start with the same
        // printable characters.
        if (HasAmbiguousBinarySignature(bytes) || IsZlibHeader(bytes) || GetControlRatio(bytes, countUndefinedWindows1251: true) > LegacyControlRatioLimit)
        {
            return TextDecodingResult.Binary;
        }

        return TryGetString(StrictWindows1251.Value, bytes, out var legacyText)
            ? new TextDecodingResult(TextContentKind.Text, legacyText, "windows-1251")
            : TextDecodingResult.Binary;
    }

    private static ReadOnlySpan<byte> TrimIncompleteTail(ReadOnlySpan<byte> sample)
    {
        if (sample.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || sample.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            // UTF-16: keep an even number of bytes.
            return sample[..(sample.Length & ~1)];
        }

        // UTF-8: drop a lead byte and its continuation bytes at the very end when the
        // sequence is shorter than the lead byte announces (at most 3 bytes are dropped).
        var index = sample.Length - 1;
        var continuationBytes = 0;
        while (index >= 0 && continuationBytes < 3 && (sample[index] & 0xC0) == 0x80)
        {
            continuationBytes++;
            index--;
        }

        if (index < 0)
        {
            return sample;
        }

        var lead = sample[index];
        var expectedLength = lead switch
        {
            >= 0xF0 and <= 0xF4 => 4,
            >= 0xE0 and <= 0xEF => 3,
            >= 0xC2 and <= 0xDF => 2,
            _ => 1
        };
        return expectedLength > 1 && continuationBytes + 1 < expectedLength ? sample[..index] : sample;
    }

    private static TextDecodingResult TryDecode(Encoding encoding, ReadOnlySpan<byte> bytes, string encodingName)
    {
        return TryGetString(encoding, bytes, out var text)
            ? new TextDecodingResult(TextContentKind.Text, text, encodingName)
            : TextDecodingResult.DecodeFailed;
    }

    private static bool TryGetString(Encoding encoding, ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = encoding.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
        catch (ArgumentException)
        {
            // Odd-length UTF-16 input and similar malformed sequences.
            text = string.Empty;
            return false;
        }
    }

    private static bool HasUnambiguousBinarySignature(ReadOnlySpan<byte> bytes)
    {
        foreach (var signature in BinarySignatures)
        {
            if (!IsAmbiguous(signature) && bytes.StartsWith(signature))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAmbiguousBinarySignature(ReadOnlySpan<byte> bytes)
    {
        foreach (var signature in BinarySignatures)
        {
            if (IsAmbiguous(signature) && bytes.StartsWith(signature))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A signature is ambiguous when it is short and consists of printable ASCII only,
    /// so an ordinary text file could start with it ("PACK", "DIRC", "MZ", "BZh").
    /// </summary>
    private static bool IsAmbiguous(byte[] signature)
    {
        if (!AmbiguousSignatureLengths.Contains(signature.Length))
        {
            return false;
        }

        foreach (var value in signature)
        {
            if (value is < 0x20 or > 0x7E)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>RFC 1950 header: CMF 0x78 (deflate, 32K window) and a valid FCHECK.</summary>
    private static bool IsZlibHeader(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length >= 2 &&
            bytes[0] == 0x78 &&
            bytes[1] is 0x01 or 0x5E or 0x9C or 0xDA &&
            ((bytes[0] << 8) | bytes[1]) % 31 == 0;
    }

    /// <summary>
    /// Share of C0 control bytes other than the usual whitespace (TAB, LF, VT, FF, CR)
    /// and ESC (used by ANSI colour sequences in logs). DEL is counted as well. For the
    /// legacy code page 0x98, which Windows-1251 leaves undefined, is counted too.
    /// </summary>
    private static double GetControlRatio(ReadOnlySpan<byte> bytes, bool countUndefinedWindows1251 = false)
    {
        var controlBytes = 0;
        foreach (var value in bytes)
        {
            if (value is < 0x09 or (> 0x0D and < 0x20 and not 0x1B) or 0x7F ||
                (countUndefinedWindows1251 && value == 0x98))
            {
                controlBytes++;
            }
        }

        return (double)controlBytes / bytes.Length;
    }
}
