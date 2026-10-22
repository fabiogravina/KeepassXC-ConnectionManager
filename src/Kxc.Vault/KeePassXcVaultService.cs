using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Kxc.Core.Entries;
using Kxc.Core.Platform;
using Kxc.Core.Security;
using Kxc.Core.Vault;

namespace Kxc.Vault;

/// <summary>
/// Accesso al database tramite keepassxc-cli, un processo per operazione (esito della fase 0):
/// "export" allo sblocco e alla ricarica, "show -s" solo al momento della connessione.
/// </summary>
public sealed class KeePassXcVaultService : IVaultService, IDisposable
{
    public static readonly Version MinimumCliVersion = new(2, 7, 0);

    private const int MaxPasswordBytes = 64 * 1024;
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ShowTimeout = TimeSpan.FromSeconds(30);

    private readonly IProcessRunner _runner;
    private readonly IKeePassXcCliLocator _locator;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Version?> _checkedCliVersions = new(StringComparer.Ordinal);
    private UnlockCredentials? _credentials;

    public KeePassXcVaultService(IProcessRunner runner, IKeePassXcCliLocator locator)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(locator);
        _runner = runner;
        _locator = locator;
    }

    public bool IsUnlocked
    {
        get
        {
            lock (_gate)
            {
                return _credentials is not null;
            }
        }
    }

    public async Task<VaultSnapshot> UnlockAsync(UnlockCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        try
        {
            CheckFiles(credentials);
            var cli = await ResolveCliAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = await ExportAsync(cli, credentials, cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                _credentials?.Dispose();
                _credentials = credentials;
            }

            return snapshot;
        }
        catch
        {
            credentials.Dispose();
            throw;
        }
    }

    public async Task<VaultSnapshot> ReloadAsync(CancellationToken cancellationToken)
    {
        var cli = await ResolveCliAsync(cancellationToken).ConfigureAwait(false);
        var credentials = CurrentCredentials();
        CheckFiles(credentials);
        return await ExportAsync(cli, credentials, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SecretBuffer> ReadPasswordAsync(ConnectionEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Warning == EntryWarning.AmbiguousPath)
        {
            throw new VaultException(VaultError.EntryNotFound, "Più voci hanno lo stesso percorso.");
        }

        var cli = await ResolveCliAsync(cancellationToken).ConfigureAwait(false);
        var credentials = CurrentCredentials();
        var arguments = KeePassXcCommands.ShowPassword(credentials.DatabasePath, credentials.KeyFilePath, credentials.Password is not null, entry.EntryPath);

        SecretBuffer? password = null;
        var outcome = await RunWithCredentialsAsync(
            new ProcessRequest(cli, arguments) { Timeout = ShowTimeout },
            async (stdout, ct) => password = await ReadSecretAsync(stdout, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        if (outcome.ExitCode != 0 || password is null)
        {
            password?.Dispose();
            throw outcome.ExitCode != 0 && outcome.StandardError.Length > 0
                ? new VaultException(VaultError.EntryNotFound, "keepassxc-cli non ha trovato la voce.")
                : MapFailure(outcome);
        }

        return password;
    }

    public void Lock()
    {
        lock (_gate)
        {
            _credentials?.Dispose();
            _credentials = null;
        }
    }

    public void Dispose() => Lock();

    private UnlockCredentials CurrentCredentials()
    {
        lock (_gate)
        {
            return _credentials ?? throw new VaultException(VaultError.Locked, "Il database è bloccato.");
        }
    }

    private static void CheckFiles(UnlockCredentials credentials)
    {
        if (!File.Exists(credentials.DatabasePath))
        {
            throw new VaultException(VaultError.DatabaseNotFound, "File del database non trovato.");
        }

        if (credentials.KeyFilePath is not null && !File.Exists(credentials.KeyFilePath))
        {
            throw new VaultException(VaultError.KeyFileNotFound, "Key file non trovato.");
        }

        KdbxFormatKind format;
        try
        {
            format = KdbxFormat.Detect(credentials.DatabasePath);
        }
        catch (IOException ex)
        {
            throw new VaultException(VaultError.ReadFailed, "Impossibile leggere il file del database.", ex);
        }

        if (!KdbxFormat.IsSupported(format))
        {
            throw new VaultException(VaultError.UnsupportedFormat, $"Formato non supportato: {format}.");
        }
    }

    private async Task<string> ResolveCliAsync(CancellationToken cancellationToken)
    {
        var cli = _locator.FindCli() ?? throw new VaultException(VaultError.CliNotFound, "keepassxc-cli non trovato.");

        Version? version;
        bool known;
        lock (_gate)
        {
            known = _checkedCliVersions.TryGetValue(cli, out version);
        }

        if (!known)
        {
            version = await ReadCliVersionAsync(cli, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _checkedCliVersions[cli] = version;
            }
        }

        if (version is null || version < MinimumCliVersion)
        {
            throw new VaultException(VaultError.CliVersionTooOld, $"keepassxc-cli {version?.ToString() ?? "?"}: serve la {MinimumCliVersion} o successiva.");
        }

        return cli;
    }

    private async Task<Version?> ReadCliVersionAsync(string cli, CancellationToken cancellationToken)
    {
        string? text = null;
        try
        {
            await _runner.RunAsync(
                new ProcessRequest(cli, KeePassXcCommands.Version()) { Timeout = ShowTimeout },
                ReadOnlyMemory<byte>.Empty,
                async (stdout, ct) =>
                {
                    using var reader = new StreamReader(stdout, Encoding.UTF8, leaveOpen: true);
                    text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new VaultException(VaultError.CliNotFound, "keepassxc-cli non si avvia.", ex);
        }

        return text is null ? null : KeePassXcCommands.ParseVersion(text);
    }

    private async Task<VaultSnapshot> ExportAsync(string cli, UnlockCredentials credentials, CancellationToken cancellationToken)
    {
        var arguments = KeePassXcCommands.ExportXml(credentials.DatabasePath, credentials.KeyFilePath, credentials.Password is not null);

        VaultSnapshot? snapshot = null;
        XmlException? parseError = null;
        var outcome = await RunWithCredentialsAsync(
            new ProcessRequest(cli, arguments) { Timeout = ExportTimeout },
            (stdout, ct) => Task.Run(
                () =>
                {
                    try
                    {
                        snapshot = KeePassXcExportParser.Parse(stdout);
                    }
                    catch (XmlException ex)
                    {
                        // Con credenziali errate stdout è vuoto: conta l'exit code, valutato dopo.
                        parseError = ex;
                    }
                },
                ct),
            cancellationToken,
            credentials).ConfigureAwait(false);

        if (outcome.ExitCode != 0)
        {
            throw MapFailure(outcome);
        }

        return snapshot ?? throw new VaultException(VaultError.ReadFailed, "Risposta di keepassxc-cli non valida.", parseError);
    }

    /// <summary>La password del database passa solo da stdin, in una copia azzerata subito dopo.</summary>
    private async Task<ProcessOutcome> RunWithCredentialsAsync(
        ProcessRequest request,
        Func<Stream, CancellationToken, Task> readStandardOutput,
        CancellationToken cancellationToken,
        UnlockCredentials? credentials = null)
    {
        SecretBuffer? stdin = null;
        lock (_gate)
        {
            credentials ??= _credentials ?? throw new VaultException(VaultError.Locked, "Il database è bloccato.");
            stdin = credentials.Password?.WithTrailingNewline();
        }

        try
        {
            return await _runner.RunAsync(request, stdin?.Memory ?? ReadOnlyMemory<byte>.Empty, readStandardOutput, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new VaultException(VaultError.Timeout, "keepassxc-cli non ha risposto in tempo.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not VaultException)
        {
            throw new VaultException(VaultError.CliNotFound, "Impossibile avviare keepassxc-cli.", ex);
        }
        finally
        {
            stdin?.Dispose();
        }
    }

    /// <summary>Exit code 1 con stderr vuoto: password errata o key file mancante (fase 0).</summary>
    private static VaultException MapFailure(ProcessOutcome outcome) =>
        outcome.ExitCode == 1 && string.IsNullOrWhiteSpace(outcome.StandardError)
            ? new VaultException(VaultError.InvalidCredentials, "Password o key file non corretti.")
            : new VaultException(VaultError.ReadFailed, $"keepassxc-cli è terminato con codice {outcome.ExitCode}.");

    /// <summary>Legge stdout in un buffer bloccato e toglie l'a capo finale aggiunto da keepassxc-cli.</summary>
    private static async Task<SecretBuffer> ReadSecretAsync(Stream stdout, CancellationToken cancellationToken)
    {
        var buffer = GC.AllocateArray<byte>(MaxPasswordBytes, pinned: true);
        try
        {
            var total = 0;
            int read;
            while ((read = await stdout.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total == buffer.Length)
                {
                    throw new VaultException(VaultError.ReadFailed, "Password troppo lunga.");
                }
            }

            if (total > 0 && buffer[total - 1] == '\n')
            {
                total--;
                if (total > 0 && buffer[total - 1] == '\r')
                {
                    total--;
                }
            }

            return SecretBuffer.FromBytes(buffer.AsSpan(0, total));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }
}
