using FilesCollector.Core.Signatures;

namespace FilesCollector.Extractors;

public sealed class StructuredDataSignatureExtractor : ISignatureExtractor
{
    public bool CanHandle(string extension)
    {
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".yml", StringComparison.OrdinalIgnoreCase);
    }

    public SignatureExtractionResult Extract(string path, string source)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? ExtractJson(source)
            : ExtractYaml(source);
    }

    private static SignatureExtractionResult ExtractJson(string source)
    {
        try
        {
            using var document = JsonDocument.Parse(source);
            var lines = new List<string>();
            RenderJson(document.RootElement, lines, 0, null);
            return new SignatureExtractionResult(true, string.Join(Environment.NewLine, lines) + Environment.NewLine, "json-structure-v1", null);
        }
        catch (JsonException)
        {
            return new SignatureExtractionResult(false, null, "json-structure-v1", "signature_extraction_failed");
        }
    }

    private static SignatureExtractionResult ExtractYaml(string source)
    {
        try
        {
            var lines = new List<string>();
            foreach (var rawLine in source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Split('#')[0].TrimEnd();
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('-'))
                {
                    continue;
                }

                var match = Regex.Match(line, """^(\s*)([^:#][^:]*):""");
                if (match.Success)
                {
                    var depth = match.Groups[1].Value.Length / 2;
                    lines.Add(new string(' ', depth * 2) + match.Groups[2].Value.Trim());
                }
            }

            return lines.Count == 0
                ? new SignatureExtractionResult(false, null, "yaml-structure-v1", "signature_extraction_failed")
                : new SignatureExtractionResult(true, string.Join(Environment.NewLine, lines) + Environment.NewLine, "yaml-structure-v1", null);
        }
        catch (Exception)
        {
            return new SignatureExtractionResult(false, null, "yaml-structure-v1", "signature_extraction_failed");
        }
    }

    private static void RenderJson(JsonElement element, List<string> lines, int depth, string? name)
    {
        if (name is not null)
        {
            lines.Add(new string(' ', depth * 2) + name + GetJsonKindSuffix(element));
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                RenderJson(property.Value, lines, depth + (name is null ? 0 : 1), property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0)
        {
            RenderJson(element[0], lines, depth + 1, "[item]");
        }
    }

    private static string GetJsonKindSuffix(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => " {}",
            JsonValueKind.Array => " []",
            JsonValueKind.String => " : string",
            JsonValueKind.Number => " : number",
            JsonValueKind.True or JsonValueKind.False => " : boolean",
            JsonValueKind.Null => " : null",
            _ => string.Empty
        };
    }
}
