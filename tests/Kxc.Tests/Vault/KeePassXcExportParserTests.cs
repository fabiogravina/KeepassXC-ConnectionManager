using System.Text;
using System.Xml;
using Kxc.Core.Vault;
using Kxc.Tests.TestData;
using Kxc.Vault;

namespace Kxc.Tests.Vault;

public class KeePassXcExportParserTests
{
    private static VaultSnapshot ParseFixture()
    {
        using var stream = File.OpenRead(Fixture.XmlPath);
        return KeePassXcExportParser.Parse(stream);
    }

    [Fact]
    public void Reads_database_name_and_entries_outside_the_recycle_bin()
    {
        var snapshot = ParseFixture();

        Assert.Equal("Fixture KXC", snapshot.DatabaseName);
        Assert.Equal(9, snapshot.Entries.Count);
        Assert.DoesNotContain(snapshot.Entries, e => e.Title == "Eliminato");
    }

    [Fact]
    public void History_versions_are_ignored()
    {
        var snapshot = ParseFixture();

        Assert.DoesNotContain(snapshot.Entries, e => e.Title == "Router vecchio");
        Assert.Single(snapshot.Entries, e => e.Title == "Router");
    }

    [Fact]
    public void Reads_groups_tags_custom_fields_and_attachments()
    {
        var snapshot = ParseFixture();
        var gateway = snapshot.Entries.Single(e => e.Title == "Gateway");
        var contabilita = snapshot.Entries.Single(e => e.Title == "Contabilità");

        Assert.Equal(["Clienti", "Rossi Srl"], gateway.GroupPath);
        Assert.Equal("Clienti/Rossi Srl/Gateway", gateway.EntryPath);
        Assert.Equal("SSH;Sftp", gateway.Tags);
        Assert.Equal("ssh://gw.rossi.example:2222", gateway.Url);
        Assert.Equal(["id_ed25519"], gateway.AttachmentNames);
        Assert.Equal(new Guid("00000000-0000-0000-0000-000000000102"), gateway.Uuid);

        Assert.Equal("10.20.0.15", contabilita.CustomFields["Host"]);
        Assert.Equal("3390", contabilita.CustomFields["Port"]);
    }

    [Fact]
    public void Reads_expiry_only_when_enabled()
    {
        var snapshot = ParseFixture();

        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), snapshot.Entries.Single(e => e.Title == "Vecchio server").ExpiresAt);
        Assert.Null(snapshot.Entries.Single(e => e.Title == "Server file").ExpiresAt);
    }

    [Fact]
    public void Protected_values_never_reach_the_model()
    {
        var snapshot = ParseFixture();

        var visible = string.Join('\n', snapshot.Entries.SelectMany(e =>
            new[] { e.Title, e.UserName, e.Url, e.Notes, e.Tags }
                .Concat(e.CustomFields.Keys)
                .Concat(e.CustomFields.Values)));

        foreach (var secret in Fixture.EntrySecrets)
        {
            Assert.DoesNotContain(secret, visible, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(snapshot.Entries, e => e.CustomFields.ContainsKey("ApiToken"));
    }

    [Theory]
    [InlineData("aRFl4g4AAAA=")]
    [InlineData("2026-10-17T07:00:31Z")]
    public void Parses_binary_and_iso_times(string value)
    {
        var parsed = KeePassXcExportParser.ParseTime(value);

        Assert.NotNull(parsed);
        Assert.Equal(2026, parsed.Value.Year);
    }

    [Fact]
    public void Empty_output_is_an_xml_error()
    {
        Assert.Throws<XmlException>(() => KeePassXcExportParser.Parse(new MemoryStream()));
    }

    [Fact]
    public void Dtd_is_rejected()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e \"x\">]><KeePassFile/>";

        Assert.Throws<XmlException>(() => KeePassXcExportParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }
}
