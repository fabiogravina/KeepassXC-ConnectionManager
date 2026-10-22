using System.Text;
using System.Xml;
using Kxc.Tests.TestData;
using Kxc.Vault;

namespace Kxc.Tests.Vault;

/// <summary>S6: password e allegati non arrivano al parser; S14: limite di profondità dei gruppi.</summary>
public class ProtectedValueFilterStreamTests
{
    /// <summary>Restituisce un byte alla volta: il filtro deve funzionare a cavallo dei blocchi.</summary>
    private sealed class OneByteStream(byte[] data) : MemoryStream(data)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(1, buffer.Length)]);

        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }

    private static string Filter(Stream input)
    {
        using var filtered = new ProtectedValueFilterStream(input);
        using var output = new MemoryStream();
        filtered.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Secrets_and_attachments_become_spaces(bool oneByteAtATime)
    {
        var xml = File.ReadAllBytes(Fixture.XmlPath);
        var filtered = Filter(oneByteAtATime ? new OneByteStream(xml) : new MemoryStream(xml));

        foreach (var secret in Fixture.EntrySecrets)
        {
            Assert.DoesNotContain(secret, filtered, StringComparison.Ordinal);
        }

        // Allegato id_ed25519 in base64 nella fixture.
        Assert.DoesNotContain("RkFLRSBLRVk", filtered, StringComparison.Ordinal);
        Assert.Equal(xml.Length, Encoding.UTF8.GetByteCount(filtered));
    }

    [Fact]
    public void Filtered_xml_gives_the_same_entries()
    {
        using var original = File.OpenRead(Fixture.XmlPath);
        using var source = File.OpenRead(Fixture.XmlPath);
        var expected = KeePassXcExportParser.Parse(original);
        var actual = KeePassXcExportParser.Parse(new ProtectedValueFilterStream(source));

        Assert.Equal(expected.DatabaseName, actual.DatabaseName);
        Assert.Equal(expected.Entries.Count, actual.Entries.Count);
        for (var i = 0; i < expected.Entries.Count; i++)
        {
            Assert.Equal(expected.Entries[i] with { CustomFields = actual.Entries[i].CustomFields, GroupPath = actual.Entries[i].GroupPath, AttachmentNames = actual.Entries[i].AttachmentNames }, actual.Entries[i]);
            Assert.Equal(expected.Entries[i].CustomFields, actual.Entries[i].CustomFields);
            Assert.Equal(expected.Entries[i].GroupPath, actual.Entries[i].GroupPath);
            Assert.Equal(expected.Entries[i].AttachmentNames, actual.Entries[i].AttachmentNames);
        }
    }

    [Fact]
    public void Password_is_blanked_even_without_memory_protection()
    {
        const string xml = "<KeePassFile><Root><Group><Name>R</Name><Entry><String><Key>Password</Key><Value>password-non-protetta</Value></String>"
            + "<String><Key>Title</Key><Value>Visibile</Value></String></Entry></Group></Root></KeePassFile>";

        var filtered = Filter(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

        Assert.DoesNotContain("password-non-protetta", filtered, StringComparison.Ordinal);
        Assert.Contains("<Value>Visibile</Value>", filtered, StringComparison.Ordinal);
    }

    [Fact]
    public void Overlong_tag_is_rejected()
    {
        var xml = "<KeePassFile><" + new string('a', 10_000) + "/></KeePassFile>";

        Assert.Throws<XmlException>(() => Filter(new MemoryStream(Encoding.UTF8.GetBytes(xml))));
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(KeePassXcExportParser.MaxGroupDepth + 5, true)]
    public void S14_deeply_nested_groups_are_rejected_without_crashing(int depth, bool rejected)
    {
        var xml = new StringBuilder("<KeePassFile><Root>");
        for (var i = 0; i < depth; i++)
        {
            xml.Append("<Group><Name>g</Name>");
        }

        for (var i = 0; i < depth; i++)
        {
            xml.Append("</Group>");
        }

        xml.Append("</Root></KeePassFile>");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml.ToString()));

        if (rejected)
        {
            Assert.Throws<XmlException>(() => KeePassXcExportParser.Parse(stream));
        }
        else
        {
            Assert.Empty(KeePassXcExportParser.Parse(stream).Entries);
        }
    }
}
