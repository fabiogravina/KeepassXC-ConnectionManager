using CommunityToolkit.Mvvm.ComponentModel;
using Kxc.App.Resources;
using Kxc.App.Services;
using Kxc.Core.Entries;
using Kxc.Core.Search;

namespace Kxc.App.ViewModels;

/// <summary>Riga della lista: una voce con un pulsante per ciascun protocollo.</summary>
public sealed partial class EntryItemViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteToolTip))]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isSelected;

    public EntryItemViewModel(EntryListItem item, bool isFavorite, string? sectionHeader)
    {
        Entry = item.Entry;
        _isFavorite = isFavorite;
        SectionHeader = sectionHeader;
        Protocols = ConnectionProtocolRules.Each(Entry.Protocols)
            .Select(p => new ProtocolButtonViewModel(this, p))
            .ToList();
    }

    public ConnectionEntry Entry { get; }

    public Guid Uuid => Entry.Uuid;

    /// <summary>Intestazione mostrata sopra la prima riga di ogni sezione; null per le altre righe.</summary>
    public string? SectionHeader { get; }

    public bool HasSectionHeader => SectionHeader is not null;

    /// <summary>«Rossi Srl · Server file»: la cartella è il cliente.</summary>
    public string DisplayTitle => Entry.GroupName.Length == 0 ? Entry.Title : Entry.GroupName + " · " + Entry.Title;

    public string HostDisplay => Entry.Host is null
        ? string.Empty
        : Entry.Port is { } port ? $"{Entry.Host}:{port}" : Entry.Host;

    public ConnectionProtocols DefaultProtocol => Entry.DefaultProtocol;

    public bool IsRdp => DefaultProtocol == ConnectionProtocols.Rdp;

    public bool IsSsh => DefaultProtocol == ConnectionProtocols.Ssh;

    public bool IsSftp => DefaultProtocol == ConnectionProtocols.Sftp;

    public IReadOnlyList<ProtocolButtonViewModel> Protocols { get; }

    public string? WarningText => Messages.For(Entry.VisibleWarning);

    public bool HasWarning => WarningText is not null;

    public string FavoriteToolTip => IsFavorite ? Strings.Favorite_Remove : Strings.Favorite_Add;

    /// <summary>Testo letto dai lettori di schermo per la riga.</summary>
    public string AccessibleName =>
        $"{DisplayTitle}, {HostDisplay}, {string.Join(", ", Protocols.Select(p => p.Label))}" + (HasWarning ? ". " + WarningText : string.Empty);
}

/// <summary>
/// Pulsante di connessione: porta sempre il nome del protocollo (RDP, SSH, SFTP), nella lista e
/// nel dettaglio, anche quando la voce ne ha uno solo.
/// </summary>
public sealed class ProtocolButtonViewModel
{
    public ProtocolButtonViewModel(EntryItemViewModel owner, ConnectionProtocols protocol)
    {
        Owner = owner;
        Protocol = protocol;
    }

    public EntryItemViewModel Owner { get; }

    public ConnectionProtocols Protocol { get; }

    public string Label => Messages.ProtocolName(Protocol);

    public string AccessibleName => Strings.Main_Connect + " " + Messages.ProtocolName(Protocol) + " " + Owner.DisplayTitle;

    public bool CanConnect => Owner.Entry.CanConnectWith(Protocol);
}
