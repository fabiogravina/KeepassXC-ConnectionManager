using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kxc.App.Resources;
using Kxc.App.Services;
using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Search;
using Kxc.Core.Settings;
using Kxc.Core.Vault;

namespace Kxc.App.ViewModels;

/// <summary>
/// Finestra principale: ricerca sempre a fuoco, filtri a sinistra, lista al centro e dettaglio
/// della voce selezionata a destra.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly TimeProvider _time;
    private readonly Action _lock;
    private readonly IConnectionLauncher _connections;
    private IReadOnlyList<ConnectionEntry> _entries;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private FilterItemViewModel? _selectedFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(Detail))]
    private EntryItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _resultsHeader = string.Empty;

    [ObservableProperty]
    private string? _emptyMessage;

    [ObservableProperty]
    private string _lockStatus = string.Empty;

    [ObservableProperty]
    private string? _connectionStatus;

    /// <summary>Esito dell'ultima ricarica del file (FR-11).</summary>
    [ObservableProperty]
    private string? _databaseStatus;

    private readonly Action _openSettings;

    public MainViewModel(VaultSnapshot snapshot, AppSettings settings, ISettingsStore store, TimeProvider time, IConnectionLauncher connections, Action lockAction, Action? openSettings = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _connections = connections;
        _openSettings = openSettings ?? (() => { });
        _settings = settings;
        _store = store;
        _time = time;
        _lock = lockAction;
        DatabasePath = settings.DatabasePath ?? string.Empty;
        DatabaseName = Path.GetFileName(DatabasePath);
        _entries = ConnectionCatalog.Build(snapshot.Entries, time.GetUtcNow());

        BuildFilters();
        SelectedFilter = Filters[0];
    }

    public string DatabaseName { get; }

    /// <summary>Percorso completo, mostrato come suggerimento sul nome del database.</summary>
    public string DatabasePath { get; }

    public ObservableCollection<FilterItemViewModel> QuickFilters { get; } = [];

    public ObservableCollection<FilterItemViewModel> FolderFilters { get; } = [];

    public IReadOnlyList<FilterItemViewModel> Filters => [.. QuickFilters, .. FolderFilters];

    public ObservableCollection<EntryItemViewModel> Items { get; } = [];

    public bool HasSelection => SelectedItem is not null;

    public EntryDetailViewModel? Detail => SelectedItem is null ? null : new EntryDetailViewModel(SelectedItem, LastConnection(SelectedItem.Uuid));

    /// <summary>Ricarica le voci dopo un cambio del file (FR-11), mantenendo ricerca e selezione.</summary>
    public void ReplaceEntries(VaultSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _entries = ConnectionCatalog.Build(snapshot.Entries, _time.GetUtcNow());
        var filter = SelectedFilter?.Filter;
        BuildFilters();
        SelectedFilter = Filters.FirstOrDefault(f => f.Filter == filter) ?? Filters[0];
        RefreshList();
    }

    public void MoveSelection(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var index = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
        SelectedItem = Items[Math.Clamp(index + delta, 0, Items.Count - 1)];
    }

    /// <summary>La ricerca è sempre globale: appena si scrive, la selezione a sinistra torna su «Tutti».</summary>
    partial void OnSearchTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && SelectedFilter is { Filter.Kind: not EntryFilterKind.All })
        {
            SelectedFilter = QuickFilters[0];
            return;
        }

        RefreshList();
    }

    partial void OnSelectedFilterChanged(FilterItemViewModel? oldValue, FilterItemViewModel? newValue)
    {
        oldValue?.IsSelected = false;
        newValue?.IsSelected = true;
        RefreshList();
    }

    [RelayCommand]
    private void SelectFilter(FilterItemViewModel? filter)
    {
        if (filter is not null)
        {
            // Una ricerca ignorerebbe il filtro: scegliere un filtro la chiude.
            SearchText = string.Empty;
            SelectedFilter = filter;
        }
    }

    partial void OnSelectedItemChanged(EntryItemViewModel? oldValue, EntryItemViewModel? newValue)
    {
        oldValue?.IsSelected = false;
        newValue?.IsSelected = true;
    }

    /// <summary>Invio e doppio click: protocollo predefinito della voce selezionata (RDP, SSH, SFTP).</summary>
    [RelayCommand]
    private Task ConnectSelectedAsync() =>
        SelectedItem is null ? Task.CompletedTask : ConnectAsync(SelectedItem, SelectedItem.DefaultProtocol);

    /// <summary>Ctrl+Invio: SFTP verso la voce selezionata, anche se è una voce SSH.</summary>
    [RelayCommand]
    private Task OpenSftpSelectedAsync()
    {
        if (SelectedItem is null || !SelectedItem.Entry.Protocols.HasFlag(ConnectionProtocols.Ssh) && !SelectedItem.Entry.Protocols.HasFlag(ConnectionProtocols.Sftp))
        {
            return Task.CompletedTask;
        }

        return ConnectAsync(SelectedItem, ConnectionProtocols.Sftp);
    }

    [RelayCommand]
    private Task ConnectProtocolAsync(ProtocolButtonViewModel? button)
    {
        if (button is null)
        {
            return Task.CompletedTask;
        }

        SelectedItem = button.Owner;
        return ConnectAsync(button.Owner, button.Protocol);
    }

    [RelayCommand]
    private void ToggleFavorite(EntryItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null)
        {
            return;
        }

        item.IsFavorite = _settings.ToggleFavorite(item.Uuid);
        _store.Save(_settings);
        UpdateFilterCounts();

        if (SearchText.Length == 0)
        {
            RefreshList();
        }
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void Lock() => _lock();

    [RelayCommand]
    private void OpenSettings() => _openSettings();

    /// <summary>FR-10: avvio in corso, avviata o errore con cosa fare. Le voci con avviso non partono.</summary>
    private async Task ConnectAsync(EntryItemViewModel item, ConnectionProtocols protocol)
    {
        var name = Messages.ProtocolName(protocol);
        if (!item.Entry.CanConnect)
        {
            ConnectionStatus = Messages.Format(Strings.Status_CannotConnect, item.DisplayTitle, item.WarningText ?? string.Empty);
            return;
        }

        if (!_connections.IsSupported(protocol))
        {
            ConnectionStatus = Messages.Format(Strings.Status_ConnectNotAvailable, name, item.DisplayTitle);
            return;
        }

        ConnectionStatus = Messages.Format(Strings.Status_Connecting, name, item.DisplayTitle);
        try
        {
            await _connections.LaunchAsync(item.Entry, protocol, CancellationToken.None);
            ConnectionStatus = Messages.Format(Strings.Status_Started, name, item.DisplayTitle);
            _settings.AddRecent(item.Uuid, _time.GetUtcNow());
            _store.Save(_settings);
            OnPropertyChanged(nameof(Detail));
        }
        catch (ConnectionException ex)
        {
            ConnectionStatus = Messages.Format(Strings.Status_CannotConnect, item.DisplayTitle, Messages.For(ex, name));
        }
    }

    private void RefreshList()
    {
        var favorites = _settings.Favorites.ToHashSet();
        var filter = SelectedFilter?.Filter ?? EntryFilter.All;
        var list = EntryListBuilder.Build(_entries, filter, SearchText, favorites, _settings.Recents);
        var previous = SelectedItem?.Uuid;

        Items.Clear();
        EntrySection? lastSection = null;
        string? lastFolder = null;
        foreach (var item in list)
        {
            var header = item.Section switch
            {
                EntrySection.Favorites or EntrySection.Recent when item.Section != lastSection => SectionHeader(item.Section),
                EntrySection.Folder when lastSection != EntrySection.Folder || item.FolderPath != lastFolder => FolderHeader(item.FolderPath),
                _ => null,
            };

            Items.Add(new EntryItemViewModel(item, favorites.Contains(item.Entry.Uuid), string.IsNullOrEmpty(header) ? null : header));
            lastSection = item.Section;
            lastFolder = item.FolderPath;
        }

        SelectedItem = Items.FirstOrDefault(i => i.Uuid == previous) is { } kept && SearchText.Length == 0
            ? kept
            : Items.FirstOrDefault();

        UpdateHeaderAndEmptyState();
    }

    private void UpdateHeaderAndEmptyState()
    {
        var query = SearchText.Trim();
        if (query.Length == 0)
        {
            ResultsHeader = string.Empty;
            EmptyMessage = _entries.Count == 0 ? Strings.Main_NoEntries : null;
            return;
        }

        ResultsHeader = Items.Count switch
        {
            0 => string.Empty,
            1 => Messages.Format(Strings.Main_OneResultFor, query),
            _ => Messages.Format(Strings.Main_ResultsFor, Items.Count, query),
        };
        EmptyMessage = Items.Count == 0 ? Messages.Format(Strings.Main_NoResultsFor, query) : null;
    }

    private static string SectionHeader(EntrySection section) => section switch
    {
        EntrySection.Favorites => Strings.Section_Favorites,
        EntrySection.Recent => Strings.Section_Recent,
        _ => string.Empty,
    };

    private static string FolderHeader(string folderPath) => folderPath.Replace("/", " / ", StringComparison.Ordinal);

    private void BuildFilters()
    {
        QuickFilters.Clear();
        QuickFilters.Add(new FilterItemViewModel(Strings.Filter_All, EntryFilter.All, FilterIcon.All));
        QuickFilters.Add(new FilterItemViewModel(Strings.Filter_Favorites, EntryFilter.Favorites, FilterIcon.Favorites));
        QuickFilters.Add(new FilterItemViewModel("RDP", EntryFilter.ForProtocol(ConnectionProtocols.Rdp), FilterIcon.Rdp));
        QuickFilters.Add(new FilterItemViewModel("SSH", EntryFilter.ForProtocol(ConnectionProtocols.Ssh), FilterIcon.Ssh));
        QuickFilters.Add(new FilterItemViewModel("SFTP", EntryFilter.ForProtocol(ConnectionProtocols.Sftp), FilterIcon.Sftp));

        // Albero delle cartelle che contengono voci connettibili, in ordine alfabetico.
        FolderFilters.Clear();
        var folders = new SortedSet<string>(StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true));
        foreach (var entry in _entries)
        {
            for (var depth = 1; depth <= entry.GroupPath.Count; depth++)
            {
                folders.Add(string.Join('/', entry.GroupPath.Take(depth)));
            }
        }

        foreach (var folder in folders)
        {
            var parts = folder.Split('/');
            FolderFilters.Add(new FilterItemViewModel(parts[^1], EntryFilter.ForFolder(folder), FilterIcon.None, parts.Length - 1));
        }

        UpdateFilterCounts();
    }

    private void UpdateFilterCounts()
    {
        var favorites = _settings.Favorites.ToHashSet();
        foreach (var filter in QuickFilters.Concat(FolderFilters))
        {
            filter.Count = _entries.Count(e => filter.Filter.Matches(e, favorites));
        }
    }

    private DateTimeOffset? LastConnection(Guid uuid) =>
        _settings.Recents.FirstOrDefault(r => r.Uuid == uuid)?.ConnectedAt;
}

/// <summary>Dettaglio della voce selezionata. Nessuna password.</summary>
public sealed class EntryDetailViewModel
{
    public EntryDetailViewModel(EntryItemViewModel item, DateTimeOffset? lastConnection)
    {
        Item = item;
        var entry = item.Entry;
        Title = entry.Title;
        FolderPath = string.Join(" / ", entry.GroupPath);
        ProtocolLabel = string.Join(" · ", ConnectionProtocolRules.Each(entry.Protocols).Select(Messages.ProtocolName));
        Host = entry.Host ?? "—";
        Port = entry.PortFor(entry.DefaultProtocol).ToString(CultureInfo.InvariantCulture);
        User = entry.UserName.Length == 0 ? "—" : entry.UserName;
        Domain = entry.Domain;
        LastConnection = lastConnection is { } at
            ? at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : Strings.Detail_Never;
        UsesSshAgent = entry.UsesSshAgent && entry.Protocols.HasFlag(ConnectionProtocols.Ssh);
    }

    public EntryItemViewModel Item { get; }

    public string Title { get; }

    public string FolderPath { get; }

    public string ProtocolLabel { get; }

    public string Host { get; }

    public string Port { get; }

    public string User { get; }

    public string? Domain { get; }

    public bool HasDomain => Domain is not null;

    public string LastConnection { get; }

    public bool UsesSshAgent { get; }

    public bool IsRdp => Item.IsRdp;

    public bool IsSsh => Item.IsSsh;

    public bool IsSftp => Item.IsSftp;
}
