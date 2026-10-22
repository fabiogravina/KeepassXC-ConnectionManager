using System.Globalization;
using System.Text.RegularExpressions;

namespace Kxc.Vault;

/// <summary>
/// Argomenti dei soli comandi di lettura usati (SEC-04). La password non compare mai qui: passa da
/// stdin. Sempre -q, così la richiesta della password non finisce su stdout.
/// </summary>
internal static partial class KeePassXcCommands
{
    public static IReadOnlyList<string> Version() => ["--version"];

    public static IReadOnlyList<string> ExportXml(string databasePath, string? keyFilePath, bool hasPassword) =>
        [.. Command("export", keyFilePath, hasPassword), "-f", "xml", databasePath];

    public static IReadOnlyList<string> ShowPassword(string databasePath, string? keyFilePath, bool hasPassword, string entryPath) =>
        [.. Command("show", keyFilePath, hasPassword), "-s", "-a", "Password", databasePath, entryPath];

    /// <summary>Prima riga di "keepassxc-cli --version", per esempio "2.7.12".</summary>
    public static Version? ParseVersion(string output)
    {
        var match = VersionRegex().Match(output);
        if (!match.Success)
        {
            return null;
        }

        var parts = match.Groups.Cast<Group>().Skip(1)
            .Select(g => g.Success ? int.Parse(g.Value, CultureInfo.InvariantCulture) : 0)
            .ToArray();
        return new Version(parts[0], parts[1], parts[2]);
    }

    private static List<string> Command(string name, string? keyFilePath, bool hasPassword)
    {
        var arguments = new List<string> { name, "-q" };
        if (keyFilePath is not null)
        {
            arguments.Add("-k");
            arguments.Add(keyFilePath);
        }

        if (!hasPassword)
        {
            arguments.Add("--no-password");
        }

        return arguments;
    }

    [GeneratedRegex(@"(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}
