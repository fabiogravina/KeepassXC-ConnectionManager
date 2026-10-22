using Kxc.Core.Connections;
using Kxc.Core.Entries;

namespace Kxc.Connectors;

/// <summary>
/// Sceglie il connettore: quello indicato nelle impostazioni se installato, altrimenti il primo
/// installato nell'ordine di registrazione. Controlla la voce prima del lancio.
/// </summary>
public sealed class ConnectionLauncher : IConnectionLauncher, IDisposable
{
    private readonly IReadOnlyList<IConnector> _connectors;
    private readonly Func<ConnectionProtocols, string?> _preferredClient;

    public ConnectionLauncher(IEnumerable<IConnector> connectors)
        : this(connectors, _ => null)
    {
    }

    public ConnectionLauncher(IEnumerable<IConnector> connectors, Func<ConnectionProtocols, string?> preferredClient)
    {
        ArgumentNullException.ThrowIfNull(connectors);
        ArgumentNullException.ThrowIfNull(preferredClient);
        _connectors = connectors.ToList();
        _preferredClient = preferredClient;
    }

    public bool IsSupported(ConnectionProtocols protocol) => Select(protocol) is not null;

    public IReadOnlyList<ClientOption> Clients(ConnectionProtocols protocol) =>
        _connectors.Where(c => c.Protocol == protocol && c.IsSupported)
            .Select(c => new ClientOption(c.ClientId, c.ClientName))
            .ToList();

    public ClientOption? ActiveClient(ConnectionProtocols protocol) =>
        Select(protocol) is { } connector ? new ClientOption(connector.ClientId, connector.ClientName) : null;

    public Task LaunchAsync(ConnectionEntry entry, ConnectionProtocols protocol, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.CanConnect || !entry.Protocols.HasFlag(protocol) && !IsSftpFromSsh(entry, protocol))
        {
            throw new ConnectionException(ConnectionError.InvalidEntry, $"La voce non permette {protocol}.");
        }

        var connector = Select(protocol)
            ?? throw new ConnectionException(ConnectionError.NotSupported, $"{protocol} non disponibile su questo sistema.");

        return connector.LaunchAsync(entry, cancellationToken);
    }

    public void CleanUp()
    {
        foreach (var connector in _connectors)
        {
            connector.CleanUp();
        }
    }

    public void Dispose() => CleanUp();

    /// <summary>FR-08: SFTP si può aprire anche dalla voce di un host SSH.</summary>
    private static bool IsSftpFromSsh(ConnectionEntry entry, ConnectionProtocols protocol) =>
        protocol == ConnectionProtocols.Sftp && entry.Protocols.HasFlag(ConnectionProtocols.Ssh);

    private IConnector? Select(ConnectionProtocols protocol)
    {
        var installed = _connectors.Where(c => c.Protocol == protocol && c.IsSupported).ToList();
        var preferred = _preferredClient(protocol);
        return installed.FirstOrDefault(c => string.Equals(c.ClientId, preferred, StringComparison.Ordinal))
            ?? installed.FirstOrDefault();
    }
}
