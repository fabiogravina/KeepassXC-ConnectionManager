using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Platform;
using Kxc.Core.Vault;

namespace Kxc.Connectors.Ssh;

/// <summary>
/// Terminale Linux noto e come gli si chiede di eseguire un comando (argomenti separati, mai una
/// stringa da interpretare con la shell).
/// </summary>
public sealed record LinuxTerminal(string Id, string Name, string Executable, Func<string, IReadOnlyList<string>, IReadOnlyList<string>> Arguments)
{
    /// <summary>Terminali supportati, in ordine di preferenza quando il desktop non ne indica uno.</summary>
    public static IReadOnlyList<LinuxTerminal> Known { get; } =
    [
        // -p tabtitle: Konsole divide le proprietà con ';', che SafeTitle toglie.
        new("konsole", "Konsole", "konsole", (title, command) => ["-p", "tabtitle=" + title, "-e", .. command]),
        new("ptyxis", "Ptyxis", "ptyxis", (_, command) => ["--new-window", "--", .. command]),
        new("kgx", "Console di GNOME", "kgx", (_, command) => ["--", .. command]),
        new("gnome-terminal", "GNOME Terminal", "gnome-terminal", (title, command) => ["--title", title, "--", .. command]),
        new("xfce4-terminal", "Xfce Terminal", "xfce4-terminal", (title, command) => ["--title", title, "-x", .. command]),
        new("kitty", "kitty", "kitty", (title, command) => ["--title", title, .. command]),
        new("alacritty", "Alacritty", "alacritty", (title, command) => ["--title", title, "-e", .. command]),
        new("foot", "foot", "foot", (title, command) => ["--title=" + title, .. command]),
        new("xterm", "XTerm", "xterm", (title, command) => ["-T", title, "-e", .. command]),
    ];

    /// <summary>
    /// Terminali installati e sicuri (S8), con il percorso trovato. Prima quelli del desktop in uso
    /// (XDG_CURRENT_DESKTOP), poi gli altri nell'ordine di <see cref="Known"/>.
    /// </summary>
    public static IReadOnlyList<(LinuxTerminal Terminal, string Path)> Installed(Func<string, bool> isSafe) =>
        Ordered(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty)
            .Select(t => (Terminal: t, Path: LinuxExecutables.Find(t.Executable, isSafe)))
            .Where(t => t.Path is not null)
            .Select(t => (t.Terminal, t.Path!))
            .ToList();

    /// <summary>Terminali noti con quelli del desktop indicato (per esempio "KDE" o "ubuntu:GNOME") in testa.</summary>
    internal static IReadOnlyList<LinuxTerminal> Ordered(string desktop)
    {
        string[] preferred = desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase) ? ["konsole"]
            : desktop.Contains("GNOME", StringComparison.OrdinalIgnoreCase) ? ["ptyxis", "kgx", "gnome-terminal"]
            : desktop.Contains("XFCE", StringComparison.OrdinalIgnoreCase) ? ["xfce4-terminal"]
            : [];

        // OrderBy è stabile: gli altri restano nell'ordine di Known.
        return Known.OrderBy(t => Array.IndexOf(preferred, t.Id) is var i and >= 0 ? i : preferred.Length).ToList();
    }
}

/// <summary>
/// SSH e SFTP su Linux con l'OpenSSH di sistema, in una nuova finestra del terminale scelto.
/// Come su Windows la password arriva a ssh solo tramite SSH_ASKPASS e il canale monouso.
/// Le variabili di SSH_ASKPASS passano da /usr/bin/env dentro il comando del terminale: molti
/// terminali (Konsole, GNOME Terminal, Ptyxis) aprono la finestra in un processo già avviato, che
/// non eredita l'ambiente dell'app.
/// </summary>
public sealed class LinuxOpenSshConnector : OpenSshConnectorBase
{
    private readonly LinuxTerminal _terminal;
    private readonly string _terminalPath;
    private readonly string? _clientPath;
    private readonly string? _envPath;

    public LinuxOpenSshConnector(
        ConnectionProtocols protocol,
        IVaultService vault,
        IProcessLauncher launcher,
        IHostKeyConfirmation hostKeys,
        string askPassPath,
        LinuxTerminal terminal,
        string terminalPath,
        string? clientPath = null,
        string? envPath = null,
        TimeSpan? channelLifetime = null,
        Func<string, bool>? isSafeExecutable = null)
        : base(protocol, vault, launcher, hostKeys, askPassPath, channelLifetime, isSafeExecutable)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalPath);
        _terminal = terminal;
        _terminalPath = terminalPath;
        _clientPath = clientPath ?? LinuxExecutables.Find(protocol == ConnectionProtocols.Ssh ? "ssh" : "sftp", IsSafeExecutable);
        _envPath = envPath ?? LinuxExecutables.Find("env", IsSafeExecutable);
    }

    public override string ClientId => "openssh-" + _terminal.Id;

    public override string ClientName => (Protocol == ConnectionProtocols.Ssh ? "OpenSSH" : "OpenSSH sftp") + " in " + _terminal.Name;

    /// <summary>S8: client, terminale ed env non devono essere sostituibili da altri utenti.</summary>
    public override bool IsSupported => !OperatingSystem.IsWindows()
        && _clientPath is not null && _envPath is not null && File.Exists(AskPassPath)
        && IsSafeExecutable(_clientPath) && IsSafeExecutable(_envPath) && IsSafeExecutable(_terminalPath);

    /// <summary>
    /// Comando eseguito dal terminale: env imposta le variabili di SSH_ASKPASS e avvia il client.
    /// Il primo elemento è un percorso assoluto, quindi nessun terminale lo scambia per un'opzione.
    /// </summary>
    internal static IReadOnlyList<string> TerminalCommand(string env, IReadOnlyDictionary<string, string> askPass, string client, IReadOnlyList<string> clientArguments) =>
    [
        env,
        .. askPass.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => v.Key + "=" + v.Value),
        client,
        .. clientArguments,
    ];

    private protected override LaunchCommand Command(string title, IReadOnlyList<string> clientArguments, IReadOnlyDictionary<string, string> askPass)
    {
        if (_clientPath is null || _envPath is null)
        {
            throw new ConnectionException(ConnectionError.ClientNotFound, "OpenSSH non trovato.");
        }

        var command = TerminalCommand(_envPath, askPass, _clientPath, clientArguments);
        return new(_terminalPath, _terminal.Arguments(SafeTitle(title), command), null);
    }
}

/// <summary>Ricerca di un eseguibile nelle cartelle di sistema e poi nel PATH (solo percorsi assoluti).</summary>
internal static class LinuxExecutables
{
    private static readonly string[] SystemDirectories = ["/usr/bin", "/bin", "/usr/local/bin"];

    public static string? Find(string name, Func<string, bool> isSafe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return SystemDirectories
            .Concat(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Where(Path.IsPathRooted))
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(candidate => File.Exists(candidate) && isSafe(candidate));
    }
}
