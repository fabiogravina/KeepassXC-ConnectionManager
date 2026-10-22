using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Kxc.Core.Entries;

/// <summary>
/// Validazione dei dati passati ai client: nessun carattere di controllo e nessun valore che inizia
/// con '-', che un client potrebbe interpretare come opzione (per esempio "-oProxyCommand=...").
/// </summary>
public static partial class ConnectionInputValidator
{
    private const int MaxFieldLength = 256;

    public static bool IsValidHost(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Length == 0 || host.StartsWith('-'))
        {
            return false;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            // IPAddress.TryParse accetta anche forme abbreviate come "10" o "1.2": solo IPv4 a quattro parti.
            return address.AddressFamily == AddressFamily.InterNetworkV6 || host.Count(c => c == '.') == 3;
        }

        return HostNameRegex().IsMatch(host);
    }

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    /// <summary>Il nome utente può essere vuoto (lo chiederà il client); se presente non può iniziare con '-'.</summary>
    public static bool IsValidUserName(string userName)
    {
        ArgumentNullException.ThrowIfNull(userName);
        return userName.Length <= MaxFieldLength
            && !userName.StartsWith('-')
            && !userName.Any(char.IsControl);
    }

    /// <summary>
    /// Nome utente per SSH e SFTP: solo lettere, cifre e <c>. _ @ \ + -</c>, senza '-' iniziale.
    /// Windows Terminal ricompone la riga di comando di ssh senza rispettare le virgolette, e un
    /// <c>ProxyCommand</c> con <c>%r</c> lo passerebbe a una shell: qualsiasi altro carattere
    /// potrebbe aggiungere opzioni a ssh (S1 dell'analisi di sicurezza). Vuoto: ssh usa l'utente locale.
    /// </summary>
    public static bool IsValidSshUserName(string userName)
    {
        ArgumentNullException.ThrowIfNull(userName);
        return userName.Length == 0 || SshUserNameRegex().IsMatch(userName);
    }

    public static bool IsValidDomain(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return domain.Length <= MaxFieldLength && DomainRegex().IsMatch(domain);
    }

    // Etichette DNS da 1 a 63 caratteri: lettere, cifre e trattini non iniziali né finali.
    [GeneratedRegex(@"^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*\.?$", RegexOptions.CultureInvariant)]
    private static partial Regex HostNameRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._@\\+][A-Za-z0-9._@\\+-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SshUserNameRegex();

    // Nome NetBIOS o dominio DNS.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex DomainRegex();
}
