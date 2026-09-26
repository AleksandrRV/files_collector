using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FilesCollector.Core.Rules;

namespace FilesCollector.Core.Presets;

/// <summary>One problem found in a preset export file.</summary>
/// <param name="Path">JSON path of the offending value, for example <c>$.extensions[2].mode</c>.</param>
/// <param name="Message">What is wrong and, where possible, how to fix it.</param>
public sealed record PresetImportError(string Path, string Message)
{
    public override string ToString()
    {
        return $"{Path}: {Message}";
    }
}

/// <summary>
/// Creates the exception thrown for invalid preset export files. It is a plain
/// <see cref="InvalidDataException"/> (that type is sealed); the individual problems are
/// attached to <see cref="Exception.Data"/> and read back with <see cref="GetErrors"/>.
/// </summary>
public static class PresetImportException
{
    private const string ErrorsKey = "FilesCollector.PresetImportErrors";

    public static InvalidDataException Create(string message, IReadOnlyList<PresetImportError> errors, Exception? innerException = null)
    {
        var exception = new InvalidDataException(message, innerException);
        exception.Data[ErrorsKey] = errors.ToArray();
        return exception;
    }

    public static InvalidDataException FromErrors(IReadOnlyList<PresetImportError> errors)
    {
        var shown = errors.Take(PresetImportValidator.MaxReportedErrors).ToArray();
        var builder = new System.Text.StringBuilder();
        builder.Append("The preset file is invalid (")
            .Append(errors.Count)
            .Append(errors.Count == 1 ? " problem):" : " problems):");
        foreach (var error in shown)
        {
            builder.Append('\n').Append("- ").Append(error);
        }

        if (errors.Count > shown.Length)
        {
            builder.Append('\n').Append("- ... ").Append(errors.Count - shown.Length).Append(" more");
        }

        return Create(builder.ToString(), errors);
    }

    /// <summary>Returns the problems attached to an exception thrown by <see cref="PresetExportData.Deserialize"/>.</summary>
    public static IReadOnlyList<PresetImportError> GetErrors(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data[ErrorsKey] as PresetImportError[] ?? [];
    }
}

/// <summary>
/// Validates the structure of a preset export file before it is deserialized.
/// </summary>
/// <remarks>
/// <para>The validator implements the same rules as
/// <c>docs/schemas/files-collector-preset-v1.schema.json</c> (the schema is meant for
/// editors; the application does not depend on a schema library). Property names are
/// taken from the <see cref="PresetExportData"/> classes, so the validator cannot drift
/// from the data model, and a test keeps the schema file in sync with both.</para>
/// <para>Rules: property names are case-insensitive; unknown and duplicate properties
/// are errors (typos would otherwise be ignored silently); enumerations are strings;
/// numbers are integers within range; paths are relative and cannot leave the scan
/// root. All problems are collected, not only the first one.</para>
/// </remarks>
public static class PresetImportValidator
{
    /// <summary>Largest collected file size: <see cref="File.ReadAllBytes(string)"/> is limited to 2 GiB.</summary>
    public const long MaxFileSizeKiB = 2L * 1024 * 1024 - 1;

    public const int MaxInventoryRefreshMinutes = 24 * 60;

    public const int MaxNameLength = 200;

    public const int MaxReportedErrors = 20;

    /// <summary>Characters Windows forbids in file names; preset names become file names of reports.</summary>
    private static readonly char[] InvalidNameCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly char[] InvalidPathCharacters = ['<', '>', ':', '"', '|', '?', '*'];

    private static readonly char[] InvalidExtensionCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*', '.'];

    /// <summary>JSON property names per section, derived from the export data classes.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> PropertyNamesBySection { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["root"] = GetJsonPropertyNames(typeof(PresetExportData)),
            ["scan"] = GetJsonPropertyNames(typeof(PresetExportData.ScanSection)),
            ["extension"] = GetJsonPropertyNames(typeof(PresetExportData.ExtensionEntry)),
            ["path"] = GetJsonPropertyNames(typeof(PresetExportData.PathEntry))
        };

    /// <summary>Returns every problem of the document; an empty list means it is valid.</summary>
    public static IReadOnlyList<PresetImportError> Validate(JsonElement root)
    {
        var errors = new List<PresetImportError>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new PresetImportError("$", $"the file must contain a JSON object, not {Describe(root)}."));
            return errors;
        }

        var properties = ReadProperties(root, "$", PropertyNamesBySection["root"], errors);
        if (properties.TryGetValue("name", out var name))
        {
            ValidateName(name, "$.name", errors);
        }

        if (properties.TryGetValue("defaultMode", out var defaultMode))
        {
            ValidateEnum<CollectionMode>(defaultMode, "$.defaultMode", errors);
        }

        if (properties.TryGetValue("scan", out var scan))
        {
            ValidateScan(scan, "$.scan", errors);
        }

        if (properties.TryGetValue("extensions", out var extensions))
        {
            ValidateExtensions(extensions, "$.extensions", errors);
        }

        if (properties.TryGetValue("paths", out var paths))
        {
            ValidatePaths(paths, "$.paths", errors);
        }

        return errors;
    }

    /// <summary>
    /// Normalizes an extension the way the planner compares it: trimmed, lower-case, with
    /// a leading dot; "[no extension]" is kept as is.
    /// </summary>
    public static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        if (string.Equals(trimmed, "[no extension]", StringComparison.OrdinalIgnoreCase))
        {
            return "[no extension]";
        }

        var lower = trimmed.ToLowerInvariant();
        return lower.StartsWith('.') ? lower : "." + lower;
    }

    private static void ValidateScan(JsonElement scan, string path, List<PresetImportError> errors)
    {
        if (!ExpectKind(scan, JsonValueKind.Object, "an object", path, errors))
        {
            return;
        }

        var properties = ReadProperties(scan, path, PropertyNamesBySection["scan"], errors);
        foreach (var booleanName in new[] { "includeAllExtensions", "includeHidden", "includeSystem", "followReparsePoints", "redactRootPath", "includeFileMetadataBlocks" })
        {
            if (properties.TryGetValue(booleanName, out var value))
            {
                ExpectBoolean(value, $"{path}.{booleanName}", errors);
            }
        }

        if (properties.TryGetValue("maxFileSizeKiB", out var maxFileSize))
        {
            ExpectInteger(maxFileSize, $"{path}.maxFileSizeKiB", 1, MaxFileSizeKiB, errors);
        }

        if (properties.TryGetValue("inventoryRefreshMinutes", out var refreshMinutes))
        {
            ExpectInteger(refreshMinutes, $"{path}.inventoryRefreshMinutes", 0, MaxInventoryRefreshMinutes, errors);
        }

        if (properties.TryGetValue("binaryFileMode", out var binaryFileMode))
        {
            ValidateEnum<CollectionMode>(binaryFileMode, $"{path}.binaryFileMode", errors);
        }

        foreach (var arrayName in new[] { "includePatterns", "excludePatterns" })
        {
            if (properties.TryGetValue(arrayName, out var patterns))
            {
                ValidateStringArray(patterns, $"{path}.{arrayName}", errors);
            }
        }

        if (properties.TryGetValue("gitIgnorePath", out var gitIgnorePath) && gitIgnorePath.ValueKind != JsonValueKind.Null)
        {
            ExpectKind(gitIgnorePath, JsonValueKind.String, "a string or null", $"{path}.gitIgnorePath", errors);
        }
    }

    private static void ValidateExtensions(JsonElement extensions, string path, List<PresetImportError> errors)
    {
        if (!ExpectKind(extensions, JsonValueKind.Array, "an array", path, errors))
        {
            return;
        }

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var entry in extensions.EnumerateArray())
        {
            var entryPath = $"{path}[{index}]";
            if (ExpectKind(entry, JsonValueKind.Object, "an object", entryPath, errors))
            {
                var properties = ReadProperties(entry, entryPath, PropertyNamesBySection["extension"], errors);
                if (!properties.TryGetValue("extension", out var extension))
                {
                    errors.Add(new PresetImportError($"{entryPath}.extension", "the property is required, for example \".png\"."));
                }
                else if (ExpectKind(extension, JsonValueKind.String, "a string", $"{entryPath}.extension", errors))
                {
                    var value = extension.GetString()!;
                    if (ValidateExtensionValue(value, $"{entryPath}.extension", errors))
                    {
                        var normalized = NormalizeExtension(value);
                        if (seen.TryGetValue(normalized, out var firstIndex))
                        {
                            errors.Add(new PresetImportError($"{entryPath}.extension", $"\"{normalized}\" is already defined at {path}[{firstIndex}]."));
                        }
                        else
                        {
                            seen[normalized] = index;
                        }
                    }
                }

                if (properties.TryGetValue("enabled", out var enabled))
                {
                    ExpectBoolean(enabled, $"{entryPath}.enabled", errors);
                }

                if (properties.TryGetValue("mode", out var mode))
                {
                    ValidateEnum<CollectionMode>(mode, $"{entryPath}.mode", errors);
                }
            }

            index++;
        }
    }

    private static bool ValidateExtensionValue(string value, string path, List<PresetImportError> errors)
    {
        var trimmed = value.Trim();
        if (string.Equals(trimmed, "[no extension]", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var body = trimmed.StartsWith('.') ? trimmed[1..] : trimmed;
        if (body.Length == 0)
        {
            errors.Add(new PresetImportError(path, "the extension is empty; use for example \".png\" or \"[no extension]\"."));
            return false;
        }

        if (body.Contains('.'))
        {
            errors.Add(new PresetImportError(path, $"only the last extension is matched; use \"{Path.GetExtension(trimmed)}\" instead of \"{trimmed}\"."));
            return false;
        }

        if (body.IndexOfAny(InvalidExtensionCharacters) >= 0 || body.Any(char.IsControl))
        {
            errors.Add(new PresetImportError(path, $"\"{value}\" contains characters that cannot appear in a file extension."));
            return false;
        }

        return true;
    }

    private static void ValidatePaths(JsonElement paths, string path, List<PresetImportError> errors)
    {
        if (!ExpectKind(paths, JsonValueKind.Array, "an array", path, errors))
        {
            return;
        }

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var entry in paths.EnumerateArray())
        {
            var entryPath = $"{path}[{index}]";
            if (ExpectKind(entry, JsonValueKind.Object, "an object", entryPath, errors))
            {
                var properties = ReadProperties(entry, entryPath, PropertyNamesBySection["path"], errors);
                var kind = PathRuleKind.Directory;
                var hasValidKind = true;
                if (properties.TryGetValue("type", out var type))
                {
                    hasValidKind = ValidateEnum(type, $"{entryPath}.type", errors, out kind);
                }

                if (!properties.TryGetValue("path", out var rulePath))
                {
                    errors.Add(new PresetImportError($"{entryPath}.path", "the property is required, for example \"src/App.cs\"."));
                }
                else if (ExpectKind(rulePath, JsonValueKind.String, "a string", $"{entryPath}.path", errors) &&
                    ValidateRelativePath(rulePath.GetString()!, kind, $"{entryPath}.path", errors) &&
                    hasValidKind)
                {
                    var key = $"{kind}:{RuleSet.NormalizeRelativePath(rulePath.GetString()!)}";
                    if (seen.TryGetValue(key, out var firstIndex))
                    {
                        errors.Add(new PresetImportError($"{entryPath}.path", $"the same {kind.ToString().ToLowerInvariant()} rule is already defined at {path}[{firstIndex}]."));
                    }
                    else
                    {
                        seen[key] = index;
                    }
                }

                if (properties.TryGetValue("mode", out var mode))
                {
                    ValidateEnum<CollectionMode>(mode, $"{entryPath}.mode", errors);
                }
            }

            index++;
        }
    }

    private static bool ValidateRelativePath(string value, PathRuleKind kind, string path, List<PresetImportError> errors)
    {
        var normalized = RuleSet.NormalizeRelativePath(value.Trim());
        if (normalized.Length == 0)
        {
            if (kind == PathRuleKind.File)
            {
                errors.Add(new PresetImportError(path, "a file rule needs a path; an empty path means the scan root and is valid for \"Directory\" rules only."));
                return false;
            }

            return true;
        }

        if (value.TrimStart().StartsWith("\\\\", StringComparison.Ordinal) || normalized.IndexOfAny(InvalidPathCharacters) >= 0 || normalized.Any(char.IsControl))
        {
            errors.Add(new PresetImportError(path, $"\"{value}\" is not a relative path; use \"/\"-separated paths relative to the scan root, for example \"src/App.cs\"."));
            return false;
        }

        if (normalized.Split('/').Any(segment => segment is "." or ".." || segment.Length == 0))
        {
            errors.Add(new PresetImportError(path, $"\"{value}\" must not contain empty, \".\" or \"..\" segments."));
            return false;
        }

        return true;
    }

    private static void ValidateName(JsonElement name, string path, List<PresetImportError> errors)
    {
        if (!ExpectKind(name, JsonValueKind.String, "a string", path, errors))
        {
            return;
        }

        var value = name.GetString()!.Trim();
        if (value.Length > MaxNameLength)
        {
            errors.Add(new PresetImportError(path, $"the name is longer than {MaxNameLength} characters."));
        }

        if (value.IndexOfAny(InvalidNameCharacters) >= 0 || value.Any(char.IsControl))
        {
            errors.Add(new PresetImportError(path, "the name contains characters that are not allowed in file names (< > : \" / \\ | ? *)."));
        }
    }

    private static void ValidateStringArray(JsonElement array, string path, List<PresetImportError> errors)
    {
        if (!ExpectKind(array, JsonValueKind.Array, "an array of strings", path, errors))
        {
            return;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            ExpectKind(item, JsonValueKind.String, "a string", $"{path}[{index}]", errors);
            index++;
        }
    }

    private static void ValidateEnum<TEnum>(JsonElement value, string path, List<PresetImportError> errors)
        where TEnum : struct, Enum
    {
        ValidateEnum<TEnum>(value, path, errors, out _);
    }

    private static bool ValidateEnum<TEnum>(JsonElement value, string path, List<PresetImportError> errors, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        var names = Enum.GetNames<TEnum>();
        var allowed = string.Join(", ", names.Select(name => $"\"{name}\""));
        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add(new PresetImportError(path, $"expected one of {allowed}, not {Describe(value)}."));
            return false;
        }

        var text = value.GetString()!;
        var match = names.FirstOrDefault(name => string.Equals(name, text.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            errors.Add(new PresetImportError(path, $"\"{text}\" is not allowed; expected one of {allowed}."));
            return false;
        }

        result = Enum.Parse<TEnum>(match);
        return true;
    }

    private static void ExpectBoolean(JsonElement value, string path, List<PresetImportError> errors)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add(new PresetImportError(path, $"expected true or false, not {Describe(value)}."));
        }
    }

    private static void ExpectInteger(JsonElement value, string path, long minimum, long maximum, List<PresetImportError> errors)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
        {
            errors.Add(new PresetImportError(path, $"expected a whole number from {minimum} to {maximum}, not {Describe(value)}."));
            return;
        }

        if (number < minimum || number > maximum)
        {
            errors.Add(new PresetImportError(path, $"{number} is out of range; expected {minimum} to {maximum}."));
        }
    }

    private static bool ExpectKind(JsonElement value, JsonValueKind kind, string expected, string path, List<PresetImportError> errors)
    {
        if (value.ValueKind == kind)
        {
            return true;
        }

        errors.Add(new PresetImportError(path, $"expected {expected}, not {Describe(value)}."));
        return false;
    }

    /// <summary>
    /// Maps the properties of an object to their canonical names (case-insensitive),
    /// reporting unknown and duplicate names.
    /// </summary>
    private static Dictionary<string, JsonElement> ReadProperties(JsonElement element, string path, IReadOnlyList<string> knownNames, List<PresetImportError> errors)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var canonical = knownNames.FirstOrDefault(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase));
            var propertyPath = $"{path}.{property.Name}";
            if (canonical is null)
            {
                var suggestion = Suggest(property.Name, knownNames);
                errors.Add(new PresetImportError(
                    propertyPath,
                    suggestion is null
                        ? $"unknown property; allowed: {string.Join(", ", knownNames)}."
                        : $"unknown property; did you mean \"{suggestion}\"?"));
                continue;
            }

            if (!result.TryAdd(canonical, property.Value))
            {
                errors.Add(new PresetImportError(propertyPath, $"\"{canonical}\" is defined more than once (property names are case-insensitive)."));
            }
        }

        return result;
    }

    private static string? Suggest(string name, IReadOnlyList<string> knownNames)
    {
        var best = knownNames
            .Select(candidate => (Candidate: candidate, Distance: GetEditDistance(name.ToLowerInvariant(), candidate.ToLowerInvariant())))
            .OrderBy(pair => pair.Distance)
            .First();
        return best.Distance <= Math.Max(2, name.Length / 4) ? best.Candidate : null;
    }

    private static int GetEditDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static string Describe(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => "null",
            JsonValueKind.True or JsonValueKind.False => $"the boolean {value.GetRawText()}",
            JsonValueKind.Number => $"the number {value.GetRawText()}",
            JsonValueKind.String => $"the string {value.GetRawText()}",
            JsonValueKind.Array => "an array",
            JsonValueKind.Object => "an object",
            _ => "an undefined value"
        };
    }

    private static IReadOnlyList<string> GetJsonPropertyNames(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .ToArray();
    }
}
