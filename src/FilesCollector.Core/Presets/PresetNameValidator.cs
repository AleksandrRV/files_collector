namespace FilesCollector.Core.Presets;

public static class PresetNameValidator
{
    public static string? Validate(string? name, IEnumerable<PresetListItem> existingPresets, Guid? currentId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "A preset name is required.";
        }

        var trimmedName = name.Trim();
        if (trimmedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "The preset name contains invalid file-name characters.";
        }

        if (existingPresets.Any(preset => preset.Id != currentId && string.Equals(preset.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return "A preset with this name already exists.";
        }

        return null;
    }
}
