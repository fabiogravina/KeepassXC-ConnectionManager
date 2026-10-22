using Kxc.Core.Entries;
using Kxc.Core.Vault;

namespace Kxc.Core.Connections;

/// <summary>Avvia una sessione con il client installato nel sistema.</summary>
public interface IConnectionLauncher
{
    bool IsSupported(ConnectionProtocols protocol);

    /// <summary>Client installati per il protocollo, nell'ordine di preferenza predefinito.</summary>
    IReadOnlyList<ClientOption> Clients(ConnectionProtocols protocol);

    /// <summary>Client usato per il protocollo: quello scelto nelle impostazioni, se installato.</summary>
    ClientOption? ActiveClient(ConnectionProtocols protocol);

    Task LaunchAsync(ConnectionEntry entry, ConnectionProtocols protocol, CancellationToken cancellationToken);

    /// <summary>Rimuove le credenziali temporanee ancora presenti (al blocco e alla chiusura).</summary>
    void CleanUp();
}

/// <summary>Client mostrato nelle impostazioni; <see cref="Id"/> è il valore salvato.</summary>
public sealed record ClientOption(string Id, string Name);

/// <summary>Un connettore per protocollo e client, con la strategia del sistema operativo corrente.</summary>
public interface IConnector
{
    ConnectionProtocols Protocol { get; }

    /// <summary>Identificativo stabile salvato nelle impostazioni, per esempio "winscp".</summary>
    string ClientId { get; }

    /// <summary>Nome del client per le impostazioni, per esempio "WinSCP".</summary>
    string ClientName { get; }

    /// <summary>Il client è installato e utilizzabile su questo sistema.</summary>
    bool IsSupported { get; }

    Task LaunchAsync(ConnectionEntry entry, CancellationToken cancellationToken);

    void CleanUp();
}

public enum ConnectionError
{
    /// <summary>La voce ha un avviso (host mancante o dati non validi).</summary>
    InvalidEntry,

    /// <summary>Protocollo non ancora disponibile su questo sistema operativo.</summary>
    NotSupported,

    ClientNotFound,
    LaunchFailed,

    /// <summary>Lettura della password non riuscita: vedi <see cref="ConnectionException.VaultError"/>.</summary>
    Vault,
}

public sealed class ConnectionException : Exception
{
    public ConnectionException()
    {
    }

    public ConnectionException(string message)
        : base(message)
    {
    }

    public ConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ConnectionException(ConnectionError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        VaultError = (innerException as VaultException)?.Error;
    }

    public ConnectionError Error { get; } = ConnectionError.LaunchFailed;

    public VaultError? VaultError { get; }
}
