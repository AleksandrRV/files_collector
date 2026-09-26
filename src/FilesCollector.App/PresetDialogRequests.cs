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

public sealed class PresetExportRequestEventArgs : EventArgs
{
    public PresetExportRequestEventArgs(string suggestedFileName)
    {
        SuggestedFileName = suggestedFileName;
    }

    public string SuggestedFileName { get; }

    public string? FilePath { get; set; }

    public bool IsAccepted { get; set; }
}

public sealed class PresetImportRequestEventArgs : EventArgs
{
    public string? FilePath { get; set; }

    public bool IsAccepted { get; set; }
}

public sealed class GitIgnoreSelectionRequestEventArgs : EventArgs
{
    public GitIgnoreSelectionRequestEventArgs(string initialDirectory)
    {
        InitialDirectory = initialDirectory;
    }

    public string InitialDirectory { get; }

    public string? FilePath { get; set; }

    public bool IsAccepted { get; set; }
}

/// <summary>A message the view shows to the user, for example as a warning dialog.</summary>
public sealed class UserMessageEventArgs : EventArgs
{
    public UserMessageEventArgs(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public string Title { get; }

    public string Message { get; }
}

/// <summary>A yes/no question; <see cref="IsConfirmed"/> stays <c>false</c> unless the user agrees.</summary>
public sealed class ConfirmationRequestEventArgs : EventArgs
{
    public ConfirmationRequestEventArgs(string title, string message)
    {
        Title = title;
        Message = message;
    }

    public string Title { get; }

    public string Message { get; }

    public bool IsConfirmed { get; set; }
}
