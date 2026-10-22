using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Kxc.Core.Security;

namespace Kxc.Connectors.Sftp;

/// <summary>
/// Named pipe che un client legge come se fosse un file (WinSCP con /passwordsfromfiles): consegna
/// il segreto una sola volta, solo all'utente corrente, e poi si chiude. Niente su disco (SEC-01).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class OneShotSecretPipe : IAsyncDisposable
{
    private readonly SecretBuffer _secret;
    private readonly CancellationTokenSource _stop;
    private readonly Task _loop;

    public OneShotSecretPipe(SecretBuffer secret, TimeSpan lifetime)
    {
        _secret = secret;
        Name = "kxc-secret-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _stop = new CancellationTokenSource(lifetime);
        _loop = ServeOnceAsync(_stop.Token);
    }

    public string Name { get; }

    /// <summary>Percorso da passare al client al posto di un file.</summary>
    public string Path => @"\\.\pipe\" + Name;

    public bool Delivered { get; private set; }

    public Task Completion => _loop;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
        _stop.Dispose();
        _secret.Dispose();
    }

    private async Task ServeOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pipe = new NamedPipeServerStream(Name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await using (pipe.ConfigureAwait(false))
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await pipe.WriteAsync(_secret.Memory, cancellationToken).ConfigureAwait(false);
                await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
                pipe.WaitForPipeDrain();
                Delivered = true;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // Scaduto, chiuso o client uscito prima di leggere.
        }
        finally
        {
            _secret.Dispose();
        }
    }
}
