using System.Text;
using System.Xml.Linq;
using Kxc.Core.Entries;
using Kxc.Core.Security;

namespace Kxc.Tests.TestData;

/// <summary>
/// Percorsi e segreti delle fixture in tests/Fixtures (copiate accanto ai test). Sta in Support/
/// e non in TestData/, che .gitignore esclude per i database personali.
/// </summary>
internal static class Fixture
{
    public const string DatabasePassword = "test";

    public const string Kdbx4MissingReason = "fixture-kdbx4.kdbx non presente (facoltativa, vedi tests/Fixtures/README.md)";

    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string DatabasePath => Path.Combine(Directory, "fixture.kdbx");

    public static string EdgeCasesDatabasePath => Path.Combine(Directory, "fixture-casi.kdbx");

    public static string Kdbx4DatabasePath => Path.Combine(Directory, "fixture-kdbx4.kdbx");

    public static string KeyFilePath => Path.Combine(Directory, "fixture.keyx");

    public static string XmlPath => Path.Combine(Directory, "fixture.xml");

    /// <summary>Valori protetti delle voci; esclusi quelli troppo corti per una ricerca sensata ("x").</summary>
    public static IReadOnlyList<string> EntrySecrets { get; } = XDocument.Load(XmlPath)
        .Descendants("Value")
        .Where(v => (string?)v.Attribute("ProtectInMemory") == "True" && v.Value.Length >= 4)
        .Select(v => v.Value)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    public static UnlockCredentials Credentials(string password = DatabasePassword, string? keyFile = null) =>
        new(DatabasePath, SecretBuffer.FromChars(password), keyFile is null ? KeyFilePath : Path.Combine(Directory, keyFile));

    public static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    /// <summary>Voce del database per i test del catalogo: per difetto una voce RDP valida con host.</summary>
    public static VaultEntry Entry(
        string title = "Server",
        string tags = "rdp",
        string userName = "user",
        string url = "",
        Dictionary<string, string>? custom = null,
        IReadOnlyList<string>? attachments = null,
        IReadOnlyList<string>? groups = null,
        DateTimeOffset? expiresAt = null) => new()
        {
            Uuid = Guid.NewGuid(),
            Title = title,
            Tags = tags,
            UserName = userName,
            Url = url,
            CustomFields = custom ?? new Dictionary<string, string> { ["Host"] = "srv.example" },
            AttachmentNames = attachments ?? [],
            GroupPath = groups ?? [],
            ExpiresAt = expiresAt,
        };
}
