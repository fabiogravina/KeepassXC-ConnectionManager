using System.Globalization;
using System.Runtime.Versioning;
using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Platform;
using Kxc.Core.Security;
using Kxc.Core.Vault;

namespace Kxc.Connectors.Sftp;

/// <summary>
/// SFTP con WinSCP (esito della fase 0, WinSCP 6.7): /passwordsfromfiles fa leggere la password
/// da un "file" che è un named pipe monouso, quindi la password non va né negli argomenti né su disco.
/// La host key di un host nuovo la conferma l'utente nella finestra di WinSCP, che mostra l'impronta.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinScpConnector : IConnector
{
    public static readonly TimeSpan PipeLifetime = TimeSpan.FromMinutes(2);

    private readonly IVaultService _vault;
    private readonly IProcessLauncher _launcher;
    private readonly Lock _gate = new();
    private readonly List<OneShotSecretPipe> _pipes = [];

    public WinScpConnector(IVaultService vault, IProcessLauncher launcher, string winScpPath)
    {
        _vault = vault;
        _launcher = launcher;
        WinScpPath = winScpPath;
    }

    public ConnectionProtocols Protocol => ConnectionProtocols.Sftp;

    public string ClientId => "winscp";

    public string ClientName => "WinSCP";

    public string WinScpPath { get; }

    public bool IsSupported => File.Exists(WinScpPath);

    /// <summary>Percorsi d'installazione di WinSCP: per tutti gli utenti o solo per l'utente.</summary>
    public static string? Find()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WinSCP", "WinSCP.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinSCP", "WinSCP.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WinSCP", "WinSCP.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task LaunchAsync(ConnectionEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var host = entry.Host ?? throw new ConnectionException(ConnectionError.InvalidEntry, "Host mancante.");

        OneShotSecretPipe? pipe = null;
        if (!entry.UsesSshAgent && entry.UserName.Length > 0)
        {
            SecretBuffer password;
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

            pipe = new OneShotSecretPipe(password, PipeLifetime);
            lock (_gate)
            {
                _pipes.RemoveAll(p => p.Completion.IsCompleted);
                _pipes.Add(pipe);
            }
        }

        try
        {
            _launcher.Start(WinScpPath, Arguments(entry, host, pipe?.Path)).Dispose();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (pipe is not null)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }

            throw new ConnectionException(ConnectionError.ClientNotFound, "WinSCP non si avvia.", ex);
        }
    }

    public void CleanUp()
    {
        OneShotSecretPipe[] pipes;
        lock (_gate)
        {
            pipes = [.. _pipes];
            _pipes.Clear();
        }

        // Mai attendere qui (S4): al blocco questo gira sul thread dell'interfaccia.
        foreach (var pipe in pipes)
        {
            pipe.Cancel();
        }
    }

    /// <summary>URL di sessione con utente codificato; la password, se c'è, la indica solo il percorso del pipe.</summary>
    internal static IReadOnlyList<string> Arguments(ConnectionEntry entry, string host, string? passwordPipePath)
    {
        var port = entry.PortFor(ConnectionProtocols.Sftp);
        var authority = host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host;
        if (port != ConnectionProtocolRules.DefaultPort(ConnectionProtocols.Sftp))
        {
            authority += ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        var user = entry.UserName.Length > 0 ? Uri.EscapeDataString(entry.UserName) + "@" : string.Empty;
        var arguments = new List<string> { $"sftp://{user}{authority}/" };
        if (passwordPipePath is not null)
        {
            arguments.Add("/passwordsfromfiles");
            arguments.Add("/password=" + passwordPipePath);
        }

        return arguments;
    }
}
