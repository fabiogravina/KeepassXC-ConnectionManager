using Kxc.Core.Entries;

namespace Kxc.Core.Search;

public enum EntryFilterKind
{
    All,
    Favorites,
    Protocol,
    Folder,
}

/// <summary>Filtro scelto nella colonna di sinistra.</summary>
public sealed record EntryFilter(EntryFilterKind Kind, ConnectionProtocols Protocol = ConnectionProtocols.None, string Folder = "")
{
    public static EntryFilter All { get; } = new(EntryFilterKind.All);

    public static EntryFilter Favorites { get; } = new(EntryFilterKind.Favorites);

    public static EntryFilter ForProtocol(ConnectionProtocols protocol) => new(EntryFilterKind.Protocol, protocol);

    /// <summary>Cartella come percorso separato da '/'; include le sottocartelle.</summary>
    public static EntryFilter ForFolder(string folderPath) => new(EntryFilterKind.Folder, Folder: folderPath);

    public bool Matches(ConnectionEntry entry, IReadOnlySet<Guid> favorites)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(favorites);

        return Kind switch
        {
            EntryFilterKind.All => true,
            EntryFilterKind.Favorites => favorites.Contains(entry.Uuid),
            EntryFilterKind.Protocol => entry.Protocols.HasFlag(Protocol),
            EntryFilterKind.Folder => IsInFolder(entry, Folder),
            _ => false,
        };
    }

    private static bool IsInFolder(ConnectionEntry entry, string folder)
    {
        var path = string.Join('/', entry.GroupPath);
        return path.Equals(folder, StringComparison.Ordinal)
            || path.StartsWith(folder + "/", StringComparison.Ordinal);
    }
}

public enum EntrySection
{
    Favorites,
    Recent,
    Folder,
    SearchResults,
}

/// <summary>Riga dell'elenco con la sezione a cui appartiene (per le intestazioni).</summary>
public sealed record EntryListItem(ConnectionEntry Entry, EntrySection Section, string FolderPath);

public sealed record RecentConnection(Guid Uuid, DateTimeOffset ConnectedAt);

/// <summary>
/// Ordine della lista. Senza ricerca: preferiti, poi recenti, poi le altre voci per cartella.
/// Con una ricerca attiva la ricerca è globale: filtro, preferiti e recenti vengono ignorati e conta
/// solo la pertinenza (decisione del 22 ottobre 2026).
/// </summary>
public static class EntryListBuilder
{
    public const int MaxRecentShown = 5;

    public static IReadOnlyList<EntryListItem> Build(
        IEnumerable<ConnectionEntry> entries,
        EntryFilter filter,
        string? query,
        IReadOnlySet<Guid> favorites,
        IReadOnlyList<RecentConnection> recents)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(recents);

        if (EntrySearch.Tokenize(query).Count > 0)
        {
            return EntrySearch.Search(entries, query)
                .Select(e => new EntryListItem(e, EntrySection.SearchResults, FolderOf(e)))
                .ToList();
        }

        var filtered = entries.Where(e => filter.Matches(e, favorites)).ToList();
        var byTitle = StringComparer.CurrentCultureIgnoreCase;
        var result = new List<EntryListItem>(filtered.Count);
        var placed = new HashSet<Guid>();

        foreach (var entry in filtered.Where(e => favorites.Contains(e.Uuid)).OrderBy(e => e.Title, byTitle))
        {
            result.Add(new EntryListItem(entry, EntrySection.Favorites, FolderOf(entry)));
            placed.Add(entry.Uuid);
        }

        var byUuid = filtered.ToDictionary(e => e.Uuid);
        var recentShown = 0;
        foreach (var recent in recents.OrderByDescending(r => r.ConnectedAt))
        {
            if (recentShown == MaxRecentShown)
            {
                break;
            }

            if (byUuid.TryGetValue(recent.Uuid, out var entry) && placed.Add(entry.Uuid))
            {
                result.Add(new EntryListItem(entry, EntrySection.Recent, FolderOf(entry)));
                recentShown++;
            }
        }

        var folderOrder = StringComparer.CurrentCultureIgnoreCase;
        foreach (var entry in filtered
                     .Where(e => !placed.Contains(e.Uuid))
                     .OrderBy(FolderOf, folderOrder)
                     .ThenBy(e => e.Title, byTitle))
        {
            result.Add(new EntryListItem(entry, EntrySection.Folder, FolderOf(entry)));
        }

        return result;
    }

    private static string FolderOf(ConnectionEntry entry) => string.Join('/', entry.GroupPath);
}
