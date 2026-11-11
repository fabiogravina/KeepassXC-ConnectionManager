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
public sealed class WindowsOpenSshConnector : OpenSshConnectorBase
{
    private readonly string _clientPath;
    private readonly string? _terminalPath;

    public WindowsOpenSshConnector(
        ConnectionProtocols protocol,
        IVaultService vault,
        IProcessLauncher launcher,
        IHostKeyConfirmation hostKeys,
        string askPassPath,
        string? clientPath = null,
        string? terminalPath = null,
        TimeSpan? channelLifetime = null,
        OpenSshTerminal terminal = OpenSshTerminal.WindowsTerminal,
        Func<string, bool>? isSafeExecutable = null)
        : base(protocol, vault, launcher, hostKeys, askPassPath, channelLifetime, isSafeExecutable)
    {
        _clientPath = clientPath ?? Path.Combine(Environment.SystemDirectory, "OpenSSH", protocol == ConnectionProtocols.Ssh ? "ssh.exe" : "sftp.exe");
        Terminal = terminal;
        _terminalPath = terminal == OpenSshTerminal.Console ? null : terminalPath ?? DefaultTerminal();
    }

    public OpenSshTerminal Terminal { get; }

    public override string ClientId => Terminal == OpenSshTerminal.WindowsTerminal ? "openssh-wt" : "openssh-console";

    public override string ClientName => (Protocol == ConnectionProtocols.Ssh ? "OpenSSH" : "OpenSSH sftp")
        + (Terminal == OpenSshTerminal.WindowsTerminal ? " in Windows Terminal" : " in console");

    /// <summary>
    /// Client e helper presenti; per Windows Terminal serve anche wt.exe. S8: client e terminale non
    /// devono essere sostituibili da altri utenti; per kxc-askpass vale l'avviso sulla cartella dell'app (S5).
    /// </summary>
    public override bool IsSupported => OperatingSystem.IsWindows() && File.Exists(_clientPath) && File.Exists(AskPassPath)
        && IsSafeExecutable(_clientPath)
        && (Terminal == OpenSshTerminal.Console || (_terminalPath is not null && IsSafeExecutable(_terminalPath)));

    /// <summary>
    /// wt.exe usa ';' per separare i comandi: va scritto come '\;' anche dentro gli argomenti.
    /// wt.exe ricompone la riga di comando del client senza rispettare le virgolette (prova di S1):
    /// un argomento con virgolette verrebbe spezzato, quindi viene rifiutato.
    /// </summary>
    internal static IReadOnlyList<string> TerminalArguments(string title, string client, IReadOnlyList<string> clientArguments)
    {
        if (clientArguments.Any(a => a.Contains('"', StringComparison.Ordinal)))
        {
            throw new ConnectionException(ConnectionError.InvalidEntry, "Argomento non ammesso per Windows Terminal.");
        }

        return
        [
            "-w", "new", "nt", "--title", EscapeForTerminal(SafeTitle(title)), client,
            .. clientArguments.Select(EscapeForTerminal),
        ];
    }

    private protected override LaunchCommand Command(string title, IReadOnlyList<string> clientArguments, IReadOnlyDictionary<string, string> askPass) =>
        _terminalPath is null
            ? new(_clientPath, clientArguments, askPass)
            : new(_terminalPath, TerminalArguments(title, _clientPath, clientArguments), askPass);

    private static string EscapeForTerminal(string value) => value.Replace(";", "\\;", StringComparison.Ordinal);

    private static string? DefaultTerminal()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(alias) ? alias : null;
    }
}
