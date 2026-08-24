namespace FilesCollector.Core.Prefixes;

public static class PrefixPresetNameValidator
{
    public static string? Validate(string? name, IEnumerable<PrefixPresetListItem> existingPresets, Guid? currentId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "A prefix preset name is required.";
        }

        var trimmedName = name.Trim();
        if (trimmedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "The prefix preset name contains invalid file-name characters.";
        }

        if (existingPresets.Any(preset => preset.Id != currentId && string.Equals(preset.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return "A prefix preset with this name already exists.";
        }

        return null;
    }
}
