using FilesCollector.Core.Signatures;

namespace FilesCollector.Extractors;

public sealed class PythonSignatureExtractor : ISignatureExtractor
{
    private static readonly Regex ImportPattern = new("""^(?:from\s+[\w.]+\s+import\s+.+|import\s+.+)$""", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex ClassPattern = new("""^class\s+[A-Za-z_]\w*(?:\([^\r\n]*\))?\s*:""", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex FunctionPattern = new("""^(?:async\s+)?def\s+(?!_)[A-Za-z_]\w*\([^\r\n]*\)(?:\s*->\s*[^:]+)?\s*:""", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex MethodPattern = new("""^\s+(?:async\s+)?def\s+(?!_)[A-Za-z_]\w*\([^\r\n]*\)(?:\s*->\s*[^:]+)?\s*:""", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public bool CanHandle(string extension)
    {
        return string.Equals(extension, ".py", StringComparison.OrdinalIgnoreCase);
    }

    public SignatureExtractionResult Extract(string path, string source)
    {
        try
        {
            var lines = new List<string>();
            AddMatches(lines, ImportPattern, source, 0);
            AddMatches(lines, ClassPattern, source, 0);
            AddMatches(lines, FunctionPattern, source, 0);
            AddMatches(lines, MethodPattern, source, 1);
            var result = lines.Distinct(StringComparer.Ordinal).ToArray();
            return result.Length == 0
                ? new SignatureExtractionResult(false, null, "python-lexical-v1", "signature_extraction_failed")
                : new SignatureExtractionResult(true, string.Join(Environment.NewLine, result) + Environment.NewLine, "python-lexical-v1", null);
        }
        catch (Exception)
        {
            return new SignatureExtractionResult(false, null, "python-lexical-v1", "signature_extraction_failed");
        }
    }

    private static void AddMatches(ICollection<string> lines, Regex pattern, string source, int indent)
    {
        foreach (Match match in pattern.Matches(source))
        {
            var value = string.Join(" ", match.Value.Trim().TrimEnd(':').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            lines.Add(new string(' ', indent * 4) + value);
        }
    }
}
