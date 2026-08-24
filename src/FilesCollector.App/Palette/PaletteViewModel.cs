using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FilesCollector.App.Palette;

public sealed class PaletteViewModel : ObservableObject
{
    public PaletteViewModel()
    {
        Entries = [];
    }

    public ObservableCollection<PaletteEntry> Entries { get; }

    public bool IsOpen { get; private set; }

    public string Query { get; private set; } = string.Empty;

    public PaletteEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            _selectedEntry = value;
            OnPropertyChanged();
        }
    }

    private PaletteEntry? _selectedEntry;

    public bool HasResults => Entries.Count > 0;

    public event EventHandler? QueryChanged;

    public event EventHandler<PaletteEntry>? ExecuteRequested;

    public event EventHandler? Opened;

    public event EventHandler? Closed;

    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        Query = string.Empty;
        OnPropertyChanged(nameof(Query));
        OnPropertyChanged(nameof(IsOpen));
        Opened?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Query = string.Empty;
        Entries.Clear();
        SelectedEntry = null;
        OnPropertyChanged(nameof(Query));
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(HasResults));
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void SetQuery(string query)
    {
        Query = query;
        OnPropertyChanged(nameof(Query));
        QueryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetEntries(IEnumerable<PaletteEntry> entries)
    {
        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(entry);
        }

        SelectedEntry = Entries.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedEntry));
        OnPropertyChanged(nameof(HasResults));
    }

    public void ExecuteSelected()
    {
        if (SelectedEntry is { } entry)
        {
            ExecuteRequested?.Invoke(this, entry);
        }
    }
}
