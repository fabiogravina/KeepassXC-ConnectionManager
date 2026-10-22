using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Platform;
using Kxc.Core.Vault;

namespace Kxc.Connectors.Ssh;

public enum OpenSshTerminal
{
    /// <summary>Nuova finestra di Windows Terminal (wt.exe).</summary>
    WindowsTerminal,

    /// <summary>Console classica di Windows, se Windows Terminal non c'è o non si vuole.</summary>
    Console,
}

/// <summary>
/// SSH e SFTP su Windows con l'OpenSSH di sistema, in una nuova finestra di Windows Terminal
/// (o in una console se Windows Terminal manca). Esito della fase 0: la password arriva a ssh solo
/// tramite SSH_ASKPASS e il canale monouso; con l'agente SSH di KeePassXC non si legge nessuna password.
/// </summary>
public sealed class WindowsOpenSshConnector : IConnector
{
    private readonly IVaultService _vault;
    private readonly IProcessLauncher _launcher;
    private readonly IHostKeyConfirmation _hostKeys;
    private readonly string _clientPath;
    private readonly string? _terminalPath;
    private readonly string _askPassPath;
    private readonly TimeSpan _channelLifetime;
    private readonly Lock _gate = new();
    private readonly List<AskPassServer> _servers = [];

    public WindowsOpenSshConnector(
        ConnectionProtocols protocol,
        IVaultService vault,
        IProcessLauncher launcher,
        IHostKeyConfirmation hostKeys,
        string askPassPath,
        string? clientPath = null,
        string? terminalPath = null,
        TimeSpan? channelLifetime = null,
        OpenSshTerminal terminal = OpenSshTerminal.WindowsTerminal)
    {
        if (protocol is not (ConnectionProtocols.Ssh or ConnectionProtocols.Sftp))
        {
            throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Solo SSH o SFTP.");
        }

        Protocol = protocol;
        _vault = vault;
        _launcher = launcher;
        _hostKeys = hostKeys;
        _askPassPath = askPassPath;
        _clientPath = clientPath ?? Path.Combine(Environment.SystemDirectory, "OpenSSH", protocol == ConnectionProtocols.Ssh ? "ssh.exe" : "sftp.exe");
        Terminal = terminal;
        _terminalPath = terminal == OpenSshTerminal.Console ? null : terminalPath ?? DefaultTerminal();
        _channelLifetime = channelLifetime ?? AskPassServer.DefaultLifetime;
    }

    public ConnectionProtocols Protocol { get; }

    public OpenSshTerminal Terminal { get; }

    public string ClientId => Terminal == OpenSshTerminal.WindowsTerminal ? "openssh-wt" : "openssh-console";

    public string ClientName => (Protocol == ConnectionProtocols.Ssh ? "OpenSSH" : "OpenSSH sftp")
        + (Terminal == OpenSshTerminal.WindowsTerminal ? " in Windows Terminal" : " in console");

    /// <summary>Client e helper presenti; per Windows Terminal serve anche wt.exe.</summary>
    public bool IsSupported => OperatingSystem.IsWindows() && File.Exists(_clientPath) && File.Exists(_askPassPath)
        && (Terminal == OpenSshTerminal.Console || _terminalPath is not null);

    /// <summary>Canale dell'ultimo avvio (per i test).</summary>
    internal AskPassServer? LastServer { get; private set; }

    public async Task LaunchAsync(ConnectionEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var host = entry.Host ?? throw new ConnectionException(ConnectionError.InvalidEntry, "Host mancante.");

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
        }

        var server = new AskPassServer(password, host, _hostKeys, _channelLifetime);
        Track(server);

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SSH_ASKPASS"] = _askPassPath,
            ["SSH_ASKPASS_REQUIRE"] = "force",
            [AskPassProtocol.ChannelVariable] = server.ChannelName,
        };

        var clientArguments = ClientArguments(Protocol, entry, host);
        var (file, arguments) = _terminalPath is null
            ? (_clientPath, clientArguments)
            : (_terminalPath, TerminalArguments(Title(entry), _clientPath, clientArguments));

        try
        {
            _launcher.Start(file, arguments, environment).Dispose();
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

        foreach (var server in servers)
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Una sola richiesta di password: con una password errata ssh si ferma invece di ripetere.
    /// "--" chiude le opzioni prima dell'host (oltre alla validazione che rifiuta il '-' iniziale).
    /// </summary>
    internal static IReadOnlyList<string> ClientArguments(ConnectionProtocols protocol, ConnectionEntry entry, string host)
    {
        var arguments = new List<string> { "-o", "NumberOfPasswordPrompts=1" };
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

    /// <summary>
    /// wt.exe usa ';' per separare i comandi: va scritto come '\;' anche dentro gli argomenti.
    /// </summary>
    internal static IReadOnlyList<string> TerminalArguments(string title, string client, IReadOnlyList<string> clientArguments) =>
        [
            "-w", "new", "nt", "--title", EscapeForTerminal(title), client,
            .. clientArguments.Select(EscapeForTerminal),
        ];

    private static string EscapeForTerminal(string value) => value.Replace(";", "\\;", StringComparison.Ordinal);

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

    private static string? DefaultTerminal()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(alias) ? alias : null;
    }
}
