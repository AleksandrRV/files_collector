namespace FilesCollector.App.Palette;

public enum PaletteEntryKind
{
    Command,
    File,
    Folder
}

public sealed record PaletteEntry(string Title, string? Detail, string? Hotkey, PaletteEntryKind Kind, Action? Action);
