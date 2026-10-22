using Kxc.Core.Entries;
using Kxc.Core.Search;

namespace Kxc.Tests.Core;

public class EntrySearchTests
{
    private static ConnectionEntry Item(string title, string[] groups, string host = "srv.example", string notes = "", string[]? tags = null) => new()
    {
        Uuid = Guid.NewGuid(),
        Title = title,
        EntryPath = string.Join('/', groups) + "/" + title,
        GroupPath = groups,
        Protocols = ConnectionProtocols.Rdp,
        Host = host,
        Notes = notes,
        Tags = tags ?? ["rdp"],
    };

    private static readonly ConnectionEntry ServerFile = Item("Server file", ["Clienti", "Rossi Srl"], "srv-file.rossi.example");
    private static readonly ConnectionEntry Contabilita = Item("Contabilità", ["Clienti", "Rossi Srl"], "10.20.0.15");
    private static readonly ConnectionEntry Nas = Item("NAS", ["Clienti", "Studio Rossi"], "nas.studiorossi.example", notes: "Backup notturni");
    private static readonly ConnectionEntry Router = Item("Router", ["Interni"], "192.168.1.1", notes: "Rack 3", tags: ["rdp", "ssh"]);
    private static readonly ConnectionEntry[] All = [ServerFile, Contabilita, Nas, Router];

    [Fact]
    public void Empty_query_returns_everything()
    {
        Assert.Equal(4, EntrySearch.Search(All, "  ").Count);
    }

    [Fact]
    public void Search_finds_the_customer_folder()
    {
        var result = EntrySearch.Search(All, "ross");

        Assert.Equal(3, result.Count);
        Assert.Contains(ServerFile, result);
        Assert.Contains(Contabilita, result);
        Assert.Contains(Nas, result);
    }

    [Fact]
    public void Search_ignores_accents_and_case()
    {
        Assert.Equal(Contabilita, Assert.Single(EntrySearch.Search(All, "CONTABILITA")));
    }

    [Fact]
    public void Every_word_must_match()
    {
        Assert.Equal(Nas, Assert.Single(EntrySearch.Search(All, "rossi nas")));
        Assert.Empty(EntrySearch.Search(All, "rossi router"));
    }

    [Fact]
    public void Title_matches_rank_above_folder_matches()
    {
        var entries = new[] { Item("Gateway", ["Server"]), Item("Server web", ["Clienti"]) };

        var result = EntrySearch.Search(entries, "server");

        Assert.Equal("Server web", result[0].Title);
    }

    [Theory]
    [InlineData("192.168")]
    [InlineData("rack")]
    [InlineData("ssh")]
    public void Search_covers_host_notes_and_tags(string query)
    {
        Assert.Equal(Router, Assert.Single(EntrySearch.Search(All, query)));
    }
}
