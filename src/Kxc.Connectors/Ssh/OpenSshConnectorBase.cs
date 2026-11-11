using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Platform;
using Kxc.Core.Vault;

namespace Kxc.Connectors.Ssh;

/// <summary>
/// Parte comune di SSH e SFTP con OpenSSH su Windows e Linux: controlli sulla voce, lettura della
/// password, canale monouso per SSH_ASKPASS e pulizia. La sottoclasse sceglie solo come aprire il
/// client in un terminale.
/// </summary>
public abstract class OpenSshConnectorBase : IConnector
{
    private readonly IVaultService _vault;
    private readonly IProcessLauncher _launcher;
    private readonly IHostKeyConfirmation _hostKeys;
    private readonly TimeSpan _channelLifetime;
    private readonly Lock _gate = new();
    private readonly List<AskPassServer> _servers = [];

    private protected OpenSshConnectorBase(
        ConnectionProtocols protocol,
        IVaultService vault,
        IProcessLauncher launcher,
        IHostKeyConfirmation hostKeys,
        string askPassPath,
        TimeSpan? channelLifetime,
        Func<string, bool>? isSafeExecutable)
    {
        if (protocol is not (ConnectionProtocols.Ssh or ConnectionProtocols.Sftp))
        {
            throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Solo SSH o SFTP.");
        }

        Protocol = protocol;
        _vault = vault;
        _launcher = launcher;
        _hostKeys = hostKeys;
        AskPassPath = askPassPath;
        IsSafeExecutable = isSafeExecutable ?? (_ => true);
        _channelLifetime = channelLifetime ?? AskPassServer.DefaultLifetime;
    }

    public ConnectionProtocols Protocol { get; }

    public abstract string ClientId { get; }

    public abstract string ClientName { get; }

    public abstract bool IsSupported { get; }

    /// <summary>Canale dell'ultimo avvio (per i test).</summary>
    internal AskPassServer? LastServer { get; private set; }

    private protected string AskPassPath { get; }

    private protected Func<string, bool> IsSafeExecutable { get; }

    public async Task LaunchAsync(ConnectionEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var host = entry.Host ?? throw new ConnectionException(ConnectionError.InvalidEntry, "Host mancante.");

        // Difesa indipendente dal catalogo (S1): niente caratteri che ssh o il terminale possano reinterpretare.
        if (!ConnectionInputValidator.IsValidSshUserName(entry.UserName) || !ConnectionInputValidator.IsValidHost(host))
        {
            throw new ConnectionException(ConnectionError.InvalidEntry, "Nome utente o host non ammessi per SSH.");
        }

        Kxc.Core.Security.SecretBuffer? password = null;
        if (!entry.UsesSshAgent && entry.UserName.Length > 0)
        {
            try
            {
                password = await _vault.ReadPasswordAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            catch (VaultException ex)
            {
                throw new ConnectionException(ConnectionError.Vault, "Lettura della password non riuscita.", ex);
            }

            // S10: se l'app si è bloccata durante la lettura, la password non va oltre.
            if (cancellationToken.IsCancellationRequested)
            {
                password.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        var server = new AskPassServer(password, entry.UserName, host, _hostKeys, _channelLifetime);
        Track(server);

        var askPass = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SSH_ASKPASS"] = AskPassPath,
            ["SSH_ASKPASS_REQUIRE"] = "force",
            [AskPassProtocol.ChannelVariable] = server.ChannelName,
        };

        string? file = null;
        try
        {
            var command = Command(Title(entry), ClientArguments(Protocol, entry, host), askPass);
            file = command.File;
            _launcher.Start(command.File, command.Arguments, command.Environment).Dispose();
        }
        catch (ConnectionException)
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw new ConnectionException(ConnectionError.ClientNotFound, $"{Path.GetFileName(file)} non si avvia.", ex);
        }
    }

    public void CleanUp()
    {
        AskPassServer[] servers;
        lock (_gate)
        {
            servers = [.. _servers];
            _servers.Clear();
        }

        // Mai attendere qui (S4): al blocco questo gira sul thread dell'interfaccia.
        foreach (var server in servers)
        {
            server.Cancel();
        }
    }

    /// <summary>
    /// Una sola richiesta di password: con una password errata ssh si ferma invece di ripetere.
    /// "--" chiude le opzioni prima dell'host (oltre alla validazione che rifiuta il '-' iniziale).
    /// </summary>
    internal static IReadOnlyList<string> ClientArguments(ConnectionProtocols protocol, ConnectionEntry entry, string host)
    {
        // ForwardAgent=no: la voce non concede l'agente SSH al server remoto (S3).
        var arguments = new List<string> { "-o", "NumberOfPasswordPrompts=1", "-o", "ForwardAgent=no" };
        if (entry.UserName.Length > 0)
        {
            arguments.Add("-o");
            arguments.Add("User=" + entry.UserName);
        }

        var port = entry.PortFor(protocol);
        if (port != ConnectionProtocolRules.DefaultPort(protocol))
        {
            arguments.Add(protocol == ConnectionProtocols.Ssh ? "-p" : "-P");
            arguments.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        arguments.Add("--");
        arguments.Add(host);
        return arguments;
    }

    /// <summary>Titolo della finestra senza caratteri che un terminale potrebbe reinterpretare.</summary>
    internal static string SafeTitle(string title)
    {
        var cleaned = new string(title.Where(c => !char.IsControl(c) && c is not ('"' or '\\' or ';' or '`')).ToArray()).Trim();
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }

    /// <summary>
    /// Programma da avviare, con i suoi argomenti e le variabili da aggiungere. Le variabili di
    /// <paramref name="askPass"/> devono arrivare a ssh; nessuna contiene segreti.
    /// </summary>
    private protected abstract LaunchCommand Command(string title, IReadOnlyList<string> clientArguments, IReadOnlyDictionary<string, string> askPass);

    private protected readonly record struct LaunchCommand(string File, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string>? Environment);

    private string Title(ConnectionEntry entry) =>
        (Protocol == ConnectionProtocols.Ssh ? "SSH · " : "SFTP · ") + (entry.GroupName.Length == 0 ? entry.Title : entry.GroupName + " · " + entry.Title);

    private void Track(AskPassServer server)
    {
        lock (_gate)
        {
            _servers.RemoveAll(s => s.Completion.IsCompleted);
            _servers.Add(server);
        }

        LastServer = server;
    }
}
