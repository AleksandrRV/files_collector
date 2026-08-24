using FilesCollector.Core.Signatures;

namespace FilesCollector.Extractors;

public sealed class JavaScriptTypeScriptSignatureExtractor : ISignatureExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".cjs", ".mjs", ".ts", ".mts", ".cts", ".tsx", ".jsx"
    };

    private static readonly Regex ImportPattern = new("""^\s*(?:import\s+.+?\s+from\s+['"].+?['"]|import\s+['"].+?['"]|export\s+.+?\s+from\s+['"].+?['"])""", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex DeclarationPattern = new("""^\s*(?:export\s+)?(?:default\s+)?(?:declare\s+)?(?:async\s+)?(?:function|class|interface|type|enum|const|let|var)\s+[A-Za-z_$][\w$]*(?:<[^>{}()]*>)?[^\n{=;]*(?=\{|=>|=|;|$)""", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex MemberPattern = new("""^\s*(?:public|protected|readonly|static|async|get|set)\s+[A-Za-z_$][\w$]*(?:<[^>{}()]*>)?\s*\([^\n{}]*\)(?:\s*:\s*[^\n{=]+)?(?=\s*\{|\s*=>|$)""", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public bool CanHandle(string extension)
    {
        return Extensions.Contains(extension);
    }

    public SignatureExtractionResult Extract(string path, string source)
    {
        try
        {
            var cleaned = RemoveComments(source);
            var lines = new List<string>();
            AddMatches(lines, ImportPattern, cleaned);
            AddMatches(lines, DeclarationPattern, cleaned);
            AddMatches(lines, MemberPattern, cleaned);
            var result = lines.Distinct(StringComparer.Ordinal).ToArray();
            return result.Length == 0
                ? new SignatureExtractionResult(false, null, "javascript-typescript-lexical-v1", "signature_extraction_failed")
                : new SignatureExtractionResult(true, string.Join(Environment.NewLine, result) + Environment.NewLine, "javascript-typescript-lexical-v1", null);
        }
        catch (Exception)
        {
            return new SignatureExtractionResult(false, null, "javascript-typescript-lexical-v1", "signature_extraction_failed");
        }
    }

    private static void AddMatches(ICollection<string> lines, Regex pattern, string source)
    {
        foreach (Match match in pattern.Matches(source))
        {
            var value = Normalize(match.Value).TrimEnd('=', ';');
            if (!string.IsNullOrWhiteSpace(value))
            {
                lines.Add(value);
            }
        }
    }

    private static string RemoveComments(string value)
    {
        var withoutBlockComments = Regex.Replace(value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutBlockComments, @"//[^\r\n]*", string.Empty);
    }

    private static string Normalize(string value)
    {
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
