using System.Globalization;

namespace Kxc.Core.Entries;

/// <summary>
/// Regole di lettura: quali voci del database diventano voci connettibili e con quali dati.
/// </summary>
public static class ConnectionCatalog
{
    private const string HostField = "Host";
    private const string PortField = "Port";
    private const string DomainField = "Domain";

    public static IReadOnlyList<ConnectionEntry> Build(IEnumerable<VaultEntry> entries, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var all = entries.ToList();
        var active = all.Where(e => !IsExpired(e, now)).ToList();

        // Il conteggio include le voci senza tag di protocollo e quelle scadute: keepassxc-cli le
        // trova comunque, e con due voci omonime restituisce la prima (prova di S2: la password della
        // voce scaduta). keepassxc-cli distingue maiuscole e minuscole, quindi il confronto è esatto.
        var pathCounts = all
            .GroupBy(e => e.EntryPath, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var result = new List<ConnectionEntry>();
        foreach (var vault in active)
        {
            var protocols = ConnectionProtocolRules.FromTags(ConnectionProtocolRules.SplitTags(vault.Tags));
            if (protocols != ConnectionProtocols.None)
            {
                result.Add(Create(vault, protocols, pathCounts[vault.EntryPath] > 1));
            }
        }

        return result;
    }

    public static bool IsExpired(VaultEntry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.ExpiresAt is { } expiry && expiry <= now;
    }

    private static ConnectionEntry Create(VaultEntry vault, ConnectionProtocols protocols, bool ambiguousPath)
    {
        var endpoint = ResolveEndpoint(vault);
        var domain = Field(vault, DomainField);

        var warning = EntryWarning.None;
        if (ambiguousPath)
        {
            warning = EntryWarning.AmbiguousPath;
        }
        else if (endpoint.Host is null)
        {
            warning = EntryWarning.MissingHost;
        }
        else if (!endpoint.PortIsValid
                 || !ConnectionInputValidator.IsValidHost(endpoint.Host)
                 || !ConnectionInputValidator.IsValidUserName(vault.UserName)
                 || (domain is not null && !ConnectionInputValidator.IsValidDomain(domain)))
        {
            warning = EntryWarning.InvalidConnectionData;
        }

        return new ConnectionEntry
        {
            Uuid = vault.Uuid,
            Title = vault.Title,
            EntryPath = vault.EntryPath,
            GroupPath = vault.GroupPath,
            Protocols = protocols,
            Host = endpoint.Host,
            Port = endpoint.Port,
            UserName = vault.UserName,
            Domain = domain,
            Notes = vault.Notes,
            Tags = ConnectionProtocolRules.SplitTags(vault.Tags),
            UsesSshAgent = vault.AttachmentNames.Contains(ConnectionEntry.SshAgentSettingsAttachment, StringComparer.Ordinal),
            SshWarning = ConnectionInputValidator.IsValidSshUserName(vault.UserName) ? EntryWarning.None : EntryWarning.UnsafeSshUserName,
            Warning = warning,
        };
    }

    internal readonly record struct Endpoint(string? Host, int? Port, bool PortIsValid);

    /// <summary>
    /// Host: campo personalizzato Host, altrimenti l'host dell'URL. Porta: campo Port, altrimenti la
    /// porta dell'URL, ma solo con schema rdp, ssh o sftp (la porta di un URL https è quella web).
    /// </summary>
    internal static Endpoint ResolveEndpoint(VaultEntry vault)
    {
        var hostField = Field(vault, HostField);
        var portField = Field(vault, PortField);

        int? port = null;
        var portIsValid = true;
        if (portField is not null)
        {
            portIsValid = int.TryParse(portField, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && ConnectionInputValidator.IsValidPort(parsed);
            port = portIsValid ? parsed : null;
        }

        if (hostField is not null)
        {
            return new Endpoint(hostField, port, portIsValid);
        }

        if (!TryParseUrl(vault.Url, out var uri))
        {
            return new Endpoint(null, port, portIsValid);
        }

        if (portField is null && !uri.IsDefaultPort && uri.Port > 0 && IsConnectionScheme(uri.Scheme))
        {
            port = uri.Port;
        }

        var host = uri.IdnHost.Trim('[', ']');
        return new Endpoint(host.Length == 0 ? null : host, port, portIsValid);
    }

    private static bool IsConnectionScheme(string scheme) => scheme is "rdp" or "ssh" or "sftp";

    private static bool TryParseUrl(string url, out Uri uri)
    {
        uri = null!;
        var trimmed = url.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            // URL senza schema, per esempio "server.example" o "10.0.0.1:3389".
            trimmed = "host://" + trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.Host))
        {
            uri = parsed;
            return true;
        }

        return false;
    }

    /// <summary>Nome del campo personalizzato senza distinguere maiuscole e minuscole.</summary>
    private static string? Field(VaultEntry vault, string name)
    {
        foreach (var (key, value) in vault.CustomFields)
        {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
