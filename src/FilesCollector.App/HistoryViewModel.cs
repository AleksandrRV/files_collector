using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilesCollector.App.History;

namespace FilesCollector.App;

public sealed partial class HistoryViewModel : ObservableObject
{
    [ObservableProperty]
    private string summary = string.Empty;

    public ObservableCollection<ReportHistoryEntry> Entries { get; } = [];

    public event EventHandler? RefreshRequested;

    public event EventHandler<ReportHistoryEntry>? OpenReportRequested;

    public event EventHandler<ReportHistoryEntry>? OpenManifestRequested;

    public event EventHandler<ReportHistoryEntry>? OpenFolderRequested;

    public event EventHandler<ReportHistoryEntry>? CopyPathRequested;

    [RelayCommand]
    private void Refresh()
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenReport(ReportHistoryEntry? entry)
    {
        if (entry is not null)
        {
            OpenReportRequested?.Invoke(this, entry);
        }
    }

    [RelayCommand]
    private void OpenManifest(ReportHistoryEntry? entry)
    {
        if (entry is not null)
        {
            OpenManifestRequested?.Invoke(this, entry);
        }
    }

    [RelayCommand]
    private void OpenFolder(ReportHistoryEntry? entry)
    {
        if (entry is not null)
        {
            OpenFolderRequested?.Invoke(this, entry);
        }
    }

    [RelayCommand]
    private void CopyPath(ReportHistoryEntry? entry)
    {
        if (entry is not null)
        {
            CopyPathRequested?.Invoke(this, entry);
        }
    }

    public void SetEntries(IReadOnlyList<ReportHistoryEntry> entries)
    {
        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(entry);
        }

        Summary = entries.Count == 0
            ? "No reports have been created yet."
            : $"{entries.Count:N0} report(s) in outputs.";
    }
}
