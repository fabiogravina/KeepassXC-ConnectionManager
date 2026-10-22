using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Platform;
using Kxc.Core.Vault;

namespace Kxc.Connectors.Rdp;

/// <summary>
/// RDP su Windows (esito della fase 0): la password va nel Gestore credenziali con CredWrite,
/// destinazione TERMSRV/&lt;host&gt;, solo per la sessione di accesso; mstsc riceve solo /v:host.
/// La credenziale si rimuove poco dopo la connessione, alla chiusura di mstsc, allo scadere
/// dell'attesa, al blocco e alla chiusura dell'app.
/// </summary>
public sealed class WindowsRdpConnector : IConnector
{
    /// <summary>Margine dopo la connessione TCP, perché mstsc completi l'autenticazione NLA.</summary>
    public static readonly TimeSpan CredentialGrace = TimeSpan.FromSeconds(15);

    /// <summary>Oltre questo tempo senza connessione la credenziale si rimuove comunque.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IVaultService _vault;
    private readonly ISystemCredentialStore _credentials;
    private readonly IProcessLauncher _launcher;
    private readonly ITcpConnectionProbe _probe;
    private readonly TimeProvider _time;
    private readonly string _mstscPath;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _pendingTargets = new(StringComparer.OrdinalIgnoreCase);

    public WindowsRdpConnector(
        IVaultService vault,
        ISystemCredentialStore credentials,
        IProcessLauncher launcher,
        ITcpConnectionProbe probe,
        TimeProvider time,
        string? mstscPath = null)
    {
        _vault = vault;
        _credentials = credentials;
        _launcher = launcher;
        _probe = probe;
        _time = time;
        _mstscPath = mstscPath ?? Path.Combine(Environment.SystemDirectory, "mstsc.exe");
    }

    public ConnectionProtocols Protocol => ConnectionProtocols.Rdp;

    public string ClientId => "mstsc";

    public string ClientName => "Desktop remoto (mstsc)";

    public bool IsSupported => OperatingSystem.IsWindows() && File.Exists(_mstscPath);

    /// <summary>Task della rimozione della credenziale dell'ultimo avvio (per i test).</summary>
    internal Task? PendingCleanup { get; private set; }

    public async Task LaunchAsync(ConnectionEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var host = entry.Host ?? throw new ConnectionException(ConnectionError.InvalidEntry, "Host mancante.");
        var port = entry.PortFor(ConnectionProtocols.Rdp);
        var userName = UserName(entry);
        var target = "TERMSRV/" + host;

        var baseline = _probe.EstablishedEndpoints(port);

        if (userName.Length > 0)
        {
            await WriteCredentialAsync(entry, target, userName, cancellationToken).ConfigureAwait(false);
        }

        ILaunchedProcess process;
        try
        {
            process = _launcher.Start(_mstscPath, Arguments(host, port));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RemoveCredential(target);
            throw new ConnectionException(ConnectionError.ClientNotFound, "mstsc non si avvia.", ex);
        }

        PendingCleanup = userName.Length > 0
            ? RemoveCredentialWhenConnectedAsync(target, host, port, baseline, process)
            : Task.CompletedTask;
    }

    public void CleanUp()
    {
        string[] targets;
        lock (_gate)
        {
            targets = [.. _pendingTargets];
        }

        foreach (var target in targets)
        {
            RemoveCredential(target);
        }
    }

    /// <summary>"/v:host" con la porta solo se diversa da 3389; IPv6 tra parentesi quadre.</summary>
    internal static IReadOnlyList<string> Arguments(string host, int port)
    {
        var address = IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{host}]"
            : host;
        return [port == ConnectionProtocolRules.DefaultPort(ConnectionProtocols.Rdp) ? "/v:" + address : $"/v:{address}:{port}"];
    }

    /// <summary>Campo Domain unito all'utente, se l'utente non contiene già il dominio.</summary>
    internal static string UserName(ConnectionEntry entry)
    {
        var user = entry.UserName.Trim();
        if (user.Length == 0 || entry.Domain is null || user.Contains('\\', StringComparison.Ordinal) || user.Contains('@', StringComparison.Ordinal))
        {
            return user;
        }

        return entry.Domain + "\\" + user;
    }

    private async Task WriteCredentialAsync(ConnectionEntry entry, string target, string userName, CancellationToken cancellationToken)
    {
        try
        {
            using var password = await _vault.ReadPasswordAsync(entry, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _pendingTargets.Add(target);
            }

            _credentials.WriteSessionCredential(target, userName, password.Span);
        }
        catch (VaultException ex)
        {
            throw new ConnectionException(ConnectionError.Vault, "Lettura della password non riuscita.", ex);
        }
        catch (Win32Exception ex)
        {
            RemoveCredential(target);
            throw new ConnectionException(ConnectionError.LaunchFailed, "Gestore credenziali non disponibile.", ex);
        }
    }

    private async Task RemoveCredentialWhenConnectedAsync(string target, string host, int port, IReadOnlySet<string> baseline, ILaunchedProcess process)
    {
        try
        {
            var deadline = _time.GetUtcNow() + ConnectTimeout;
            while (_time.GetUtcNow() < deadline && !process.HasExited)
            {
                if (HasNewConnection(host, port, baseline))
                {
                    await Task.Delay(CredentialGrace, _time).ConfigureAwait(false);
                    break;
                }

                await Task.Delay(PollInterval, _time).ConfigureAwait(false);
            }
        }
        finally
        {
            RemoveCredential(target);
            process.Dispose();
        }
    }

    /// <summary>
    /// Connessione nuova verso la porta: se l'host è un indirizzo IP deve coincidere, altrimenti
    /// basta che sia comparsa dopo l'avvio (l'app non risolve nomi per conto suo).
    /// </summary>
    private bool HasNewConnection(string host, int port, IReadOnlySet<string> baseline)
    {
        var fresh = _probe.EstablishedEndpoints(port).Where(e => !baseline.Contains(e));
        if (!IPAddress.TryParse(host, out var ip))
        {
            return fresh.Any();
        }

        var expected = new IPEndPoint(ip, port).ToString();
        return fresh.Any(e => string.Equals(e, expected, StringComparison.OrdinalIgnoreCase));
    }

    private void RemoveCredential(string target)
    {
        lock (_gate)
        {
            if (!_pendingTargets.Remove(target))
            {
                return;
            }
        }

        _credentials.Delete(target);
    }
}
