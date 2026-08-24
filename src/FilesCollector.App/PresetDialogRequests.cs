namespace FilesCollector.App;

public enum UnsavedChangesDecision
{
    Save,
    Discard,
    Cancel
}

public sealed class PresetNameRequestEventArgs : EventArgs
{
    public PresetNameRequestEventArgs(string title, string prompt, string initialName)
    {
        Title = title;
        Prompt = prompt;
        InitialName = initialName;
    }

    public string Title { get; }

    public string Prompt { get; }

    public string InitialName { get; }

    public bool IsAccepted { get; set; }

    public string? Name { get; set; }
}

public sealed class UnsavedChangesRequestEventArgs : EventArgs
{
    public UnsavedChangesRequestEventArgs(string presetName)
    {
        PresetName = presetName;
    }

    public string PresetName { get; }

    public UnsavedChangesDecision Decision { get; set; } = UnsavedChangesDecision.Cancel;
}
