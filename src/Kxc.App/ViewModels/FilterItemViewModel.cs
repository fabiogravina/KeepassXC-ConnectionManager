using CommunityToolkit.Mvvm.ComponentModel;
using Kxc.Core.Search;

namespace Kxc.App.ViewModels;

public enum FilterIcon
{
    None,
    All,
    Favorites,
    Rdp,
    Ssh,
    Sftp,
}

/// <summary>Voce della colonna di sinistra: filtro rapido o cartella del database.</summary>
public sealed partial class FilterItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isSelected;

    public FilterItemViewModel(string label, EntryFilter filter, FilterIcon icon, int depth = 0)
    {
        Label = label;
        Filter = filter;
        Icon = icon;
        Depth = depth;
    }

    public string Label { get; }

    public EntryFilter Filter { get; }

    public FilterIcon Icon { get; }

    public int Depth { get; }

    public bool IsFolder => Filter.Kind == EntryFilterKind.Folder;

    public bool HasIcon => Icon != FilterIcon.None;

    public bool IsAllIcon => Icon == FilterIcon.All;

    public bool IsFavoritesIcon => Icon == FilterIcon.Favorites;

    public bool IsRdpIcon => Icon == FilterIcon.Rdp;

    public bool IsSshIcon => Icon == FilterIcon.Ssh;

    public bool IsSftpIcon => Icon == FilterIcon.Sftp;

    public Avalonia.Thickness Indent => new(Depth * 16, 0, 0, 0);

    public string AccessibleName => $"{Label}, {Count}";
}
