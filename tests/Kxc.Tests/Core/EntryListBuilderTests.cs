using Kxc.Core.Entries;
using Kxc.Core.Search;

namespace Kxc.Tests.Core;

public class EntryListBuilderTests
{
    private static ConnectionEntry Item(string title, string folder, ConnectionProtocols protocols = ConnectionProtocols.Rdp) => new()
    {
        Uuid = Guid.NewGuid(),
        Title = title,
        EntryPath = folder + "/" + title,
        GroupPath = folder.Split('/'),
        Protocols = protocols,
        Host = "srv.example",
    };

    private static readonly ConnectionEntry Alfa = Item("Alfa", "Clienti/Rossi Srl");
    private static readonly ConnectionEntry Beta = Item("Beta", "Clienti/Bianchi SpA", ConnectionProtocols.Ssh);
    private static readonly ConnectionEntry Gamma = Item("Gamma", "Interni", ConnectionProtocols.Ssh | ConnectionProtocols.Sftp);
    private static readonly ConnectionEntry Rossi = Item("Rossi web", "Interni");
    private static readonly ConnectionEntry[] All = [Alfa, Beta, Gamma, Rossi];

    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Without_search_favorites_then_recents_then_folders()
    {
        var favorites = new HashSet<Guid> { Gamma.Uuid };
        var recents = new[] { new RecentConnection(Beta.Uuid, T0), new RecentConnection(Gamma.Uuid, T0.AddMinutes(1)) };

        var list = EntryListBuilder.Build(All, EntryFilter.All, null, favorites, recents);

        Assert.Equal([Gamma, Beta, Alfa, Rossi], list.Select(i => i.Entry));
        Assert.Equal([EntrySection.Favorites, EntrySection.Recent, EntrySection.Folder, EntrySection.Folder], list.Select(i => i.Section));
    }

    [Fact]
    public void With_search_favorites_and_recents_are_ignored()
    {
        var favorites = new HashSet<Guid> { Alfa.Uuid };
        var recents = new[] { new RecentConnection(Alfa.Uuid, T0) };

        var list = EntryListBuilder.Build(All, EntryFilter.All, "rossi", favorites, recents);

        Assert.All(list, i => Assert.Equal(EntrySection.SearchResults, i.Section));
        Assert.Equal(Rossi, list[0].Entry);
    }

    [Fact]
    public void Search_is_global_whatever_the_filter()
    {
        var list = EntryListBuilder.Build(All, EntryFilter.ForFolder("Clienti"), "rossi", new HashSet<Guid>(), []);

        Assert.Contains(list, i => i.Entry == Rossi);
        Assert.All(list, i => Assert.Equal(EntrySection.SearchResults, i.Section));
    }

    [Fact]
    public void Protocol_filter_keeps_entries_with_that_tag()
    {
        var list = EntryListBuilder.Build(All, EntryFilter.ForProtocol(ConnectionProtocols.Ssh), null, new HashSet<Guid>(), []);

        Assert.Equal([Beta, Gamma], list.Select(i => i.Entry));
    }

    [Fact]
    public void Folder_filter_includes_subfolders_only_of_that_folder()
    {
        var list = EntryListBuilder.Build(All, EntryFilter.ForFolder("Clienti"), null, new HashSet<Guid>(), []);

        Assert.Equal([Beta, Alfa], list.Select(i => i.Entry));
        Assert.Empty(EntryListBuilder.Build(All, EntryFilter.ForFolder("Client"), null, new HashSet<Guid>(), []));
    }

    [Fact]
    public void Favorites_filter_shows_only_favorites()
    {
        var list = EntryListBuilder.Build(All, EntryFilter.Favorites, null, new HashSet<Guid> { Beta.Uuid }, []);

        Assert.Equal(Beta, Assert.Single(list).Entry);
    }

    [Fact]
    public void At_most_five_recents_are_shown_at_the_top()
    {
        var many = Enumerable.Range(0, 8).Select(i => Item($"Host {i}", "Clienti")).ToList();
        var recents = many.Select((e, i) => new RecentConnection(e.Uuid, T0.AddMinutes(i))).ToList();

        var list = EntryListBuilder.Build(many, EntryFilter.All, null, new HashSet<Guid>(), recents);

        Assert.Equal(EntryListBuilder.MaxRecentShown, list.Count(i => i.Section == EntrySection.Recent));
        Assert.Equal("Host 7", list[0].Entry.Title);
    }
}
