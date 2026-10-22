using Kxc.Core.Entries;
using static Kxc.Tests.TestData.Fixture;

namespace Kxc.Tests.Core;

public class ConnectionCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Entries_without_protocol_tags_are_not_shown()
    {
        var result = ConnectionCatalog.Build([Entry(tags: "web"), Entry(title: "Server rdp", tags: "rdp")], Now);

        Assert.Equal("Server rdp", Assert.Single(result).Title);
    }

    [Fact]
    public void Expired_entries_are_not_shown()
    {
        var result = ConnectionCatalog.Build(
            [Entry(title: "Scaduta", expiresAt: Now.AddDays(-1)), Entry(title: "Valida", expiresAt: Now.AddDays(1))],
            Now);

        Assert.Equal("Valida", Assert.Single(result).Title);
    }

    [Fact]
    public void Host_field_wins_over_url()
    {
        var entry = ConnectionCatalog.Build(
            [Entry(url: "https://altro.example", custom: new() { ["Host"] = "srv.example", ["Port"] = "3390" })], Now)[0];

        Assert.Equal("srv.example", entry.Host);
        Assert.Equal(3390, entry.Port);
        Assert.Equal(EntryWarning.None, entry.Warning);
    }

    [Theory]
    [InlineData("ssh://gw.example:2222", "gw.example", 2222)]
    [InlineData("https://nas.example:5001/", "nas.example", null)]
    [InlineData("srv.example", "srv.example", null)]
    [InlineData("10.0.0.1:3389", "10.0.0.1", null)]
    [InlineData("rdp://[fe80::1]:3390", "fe80::1", 3390)]
    public void Host_comes_from_url_and_port_only_for_connection_schemes(string url, string host, int? port)
    {
        var entry = ConnectionCatalog.Build([Entry(url: url, custom: [])], Now)[0];

        Assert.Equal(host, entry.Host);
        Assert.Equal(port, entry.Port);
    }

    [Fact]
    public void Custom_field_names_are_case_insensitive()
    {
        var entry = ConnectionCatalog.Build([Entry(custom: new() { ["host"] = "srv.example", ["DOMAIN"] = "ROSSI" })], Now)[0];

        Assert.Equal("srv.example", entry.Host);
        Assert.Equal("ROSSI", entry.Domain);
    }

    [Fact]
    public void Entry_without_host_is_shown_with_a_warning()
    {
        var entry = Assert.Single(ConnectionCatalog.Build([Entry(custom: [])], Now));

        Assert.Equal(EntryWarning.MissingHost, entry.Warning);
        Assert.False(entry.CanConnect);
    }

    [Theory]
    [InlineData("-oProxyCommand=calc", "user", "3389")]
    [InlineData("srv.example", "-oProxyCommand=calc", "3389")]
    [InlineData("srv.example", "user", "99999")]
    [InlineData("srv.example", "user", "abc")]
    [InlineData("srv\n.example", "user", "22")]
    public void Invalid_connection_data_blocks_the_connection(string host, string user, string port)
    {
        var entry = ConnectionCatalog.Build([Entry(userName: user, custom: new() { ["Host"] = host, ["Port"] = port })], Now)[0];

        Assert.Equal(EntryWarning.InvalidConnectionData, entry.Warning);
    }

    [Fact]
    public void Two_entries_with_the_same_path_are_both_ambiguous()
    {
        var result = ConnectionCatalog.Build(
            [Entry(title: "Server"), Entry(title: "Server", tags: "web"), Entry(title: "Altro")], Now);

        Assert.Equal(EntryWarning.AmbiguousPath, result.Single(e => e.Title == "Server").Warning);
        Assert.Equal(EntryWarning.None, result.Single(e => e.Title == "Altro").Warning);
    }

    [Fact]
    public void Expired_entry_with_the_same_path_makes_the_valid_one_ambiguous()
    {
        // S2: keepassxc-cli restituirebbe la password della voce scaduta (prova del 22/10/2026).
        var result = ConnectionCatalog.Build(
            [Entry(title: "Server", expiresAt: Now.AddDays(-1)), Entry(title: "Server")], Now);

        Assert.Equal(EntryWarning.AmbiguousPath, Assert.Single(result).Warning);
    }

    [Fact]
    public void Unsafe_ssh_user_blocks_ssh_and_sftp_but_not_rdp()
    {
        var entry = ConnectionCatalog.Build([Entry(tags: "rdp,ssh,sftp", userName: "x\" -oProxyCommand=calc \"")], Now)[0];

        Assert.Equal(EntryWarning.None, entry.Warning);
        Assert.Equal(EntryWarning.UnsafeSshUserName, entry.SshWarning);
        Assert.Equal(EntryWarning.UnsafeSshUserName, entry.VisibleWarning);
        Assert.True(entry.CanConnectWith(ConnectionProtocols.Rdp));
        Assert.False(entry.CanConnectWith(ConnectionProtocols.Ssh));
        Assert.False(entry.CanConnectWith(ConnectionProtocols.Sftp));
    }

    [Fact]
    public void Rdp_only_entry_does_not_show_the_ssh_warning()
    {
        var entry = ConnectionCatalog.Build([Entry(tags: "rdp", userName: "Mario Rossi")], Now)[0];

        Assert.Equal(EntryWarning.None, entry.VisibleWarning);
        Assert.True(entry.CanConnectWith(ConnectionProtocols.Rdp));
    }

    [Fact]
    public void Ssh_agent_is_used_only_when_keepassxc_agent_settings_are_attached()
    {
        var result = ConnectionCatalog.Build(
            [
                Entry(title: "Con agente", tags: "ssh", attachments: ["id_ed25519", "KeeAgent.settings"]),
                Entry(title: "Solo allegato", tags: "ssh", attachments: ["id_ed25519"]),
            ],
            Now);

        Assert.True(result.Single(e => e.Title == "Con agente").UsesSshAgent);
        Assert.False(result.Single(e => e.Title == "Solo allegato").UsesSshAgent);
    }

    [Fact]
    public void Entry_path_joins_groups_and_title()
    {
        var entry = ConnectionCatalog.Build([Entry(title: "NAS", groups: ["Clienti", "Studio Rossi"])], Now)[0];

        Assert.Equal("Clienti/Studio Rossi/NAS", entry.EntryPath);
        Assert.Equal("Studio Rossi", entry.GroupName);
    }
}
