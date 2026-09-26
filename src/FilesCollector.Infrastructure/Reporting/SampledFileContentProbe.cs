using System.Collections.Concurrent;
using FilesCollector.Core.FileSystem;

namespace FilesCollector.Infrastructure.Reporting;

/// <summary>
/// <see cref="IFileContentProbe"/> that reads the first <see cref="SampleSize"/> bytes of
/// a file and classifies them with <see cref="TextContentDecoder"/>, the same detector the
/// report writer uses. Results are kept in memory for the lifetime of the application.
/// </summary>
public sealed class SampledFileContentProbe : IFileContentProbe
{
    public const int SampleSize = 8 * 1024;

    private readonly ConcurrentDictionary<string, (long? SizeBytes, bool IsBinary)> _results = new(StringComparer.OrdinalIgnoreCase);

    public bool? TryGetCachedIsBinary(string fullPath, long? sizeBytes)
    {
        return _results.TryGetValue(fullPath, out var result) && result.SizeBytes == sizeBytes
            ? result.IsBinary
            : null;
    }

    public bool ProbeIsBinary(string fullPath, long? sizeBytes)
    {
        var isBinary = ReadAndClassify(fullPath);
        _results[fullPath] = (sizeBytes, isBinary);
        return isBinary;
    }

    private static bool ReadAndClassify(string fullPath)
    {
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            var buffer = new byte[SampleSize];
            var length = 0;
            int read;
            while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0)
            {
                length += read;
            }

            var isCompleteFile = length < buffer.Length || stream.Position >= stream.Length;
            return TextContentDecoder.IsBinarySample(buffer.AsSpan(0, length), isCompleteFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
