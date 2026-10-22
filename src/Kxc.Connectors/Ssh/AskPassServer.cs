using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Kxc.Core.Connections;
using Kxc.Core.Security;

namespace Kxc.Connectors.Ssh;

/// <summary>
/// Canale monouso per un avvio di ssh o sftp: named pipe (socket Unix su Linux) accessibile solo
/// all'utente corrente. Consegna la password una sola volta e poi si chiude; gira la domanda sulla
/// host key all'utente; rifiuta tutto il resto. Si chiude comunque allo scadere della durata.
/// </summary>
internal sealed class AskPassServer : IAsyncDisposable
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(2);

    private readonly SecretBuffer? _password;
    private readonly string _userName;
    private readonly string _host;
    private readonly IHostKeyConfirmation _hostKeys;
    private readonly CancellationTokenSource _stop;
    private readonly Task _loop;

    /// <param name="password">Password della voce, di cui il server diventa proprietario; null con l'agente SSH.</param>
    /// <param name="userName">Utente della voce: la password va solo a una domanda per <c>utente@host</c> (S3).</param>
    public AskPassServer(SecretBuffer? password, string userName, string host, IHostKeyConfirmation hostKeys, TimeSpan lifetime)
    {
        _password = password;
        _userName = userName;
        _host = host;
        _hostKeys = hostKeys;
        ChannelName = "kxc-askpass-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _stop = new CancellationTokenSource(lifetime);
        _loop = RunAsync(_stop.Token);
    }

    public string ChannelName { get; }

    public bool PasswordDelivered { get; private set; }

    /// <summary>Termina quando il canale si chiude (password consegnata, scadenza o Dispose).</summary>
    public Task Completion => _loop;

    /// <summary>
    /// Chiusura immediata, senza attendere (S4): chi chiama può essere il thread dell'interfaccia,
    /// lo stesso che deve chiudere la finestra della host key. La password si azzera subito; il
    /// canale termina in background.
    /// </summary>
    public void Cancel()
    {
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Già chiuso.
        }

        _password?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Chiusura richiesta.
        }

        _stop.Dispose();
        _password?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !PasswordDelivered)
            {
                var pipe = new NamedPipeServerStream(
                    ChannelName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await using (pipe.ConfigureAwait(false))
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    await HandleAsync(pipe, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Durata scaduta o chiusura: l'helper non trova più il canale e ssh si ferma.
        }
        finally
        {
            _password?.Dispose();
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var buffer = new byte[AskPassProtocol.MaxMessageBytes];
        string prompt;
        try
        {
            var length = await AskPassProtocol.ReadMessageAsync(pipe, buffer, cancellationToken).ConfigureAwait(false);
            prompt = Encoding.UTF8.GetString(buffer, 0, length);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            return;
        }

        switch (AskPassPrompt.Classify(prompt))
        {
            case AskPassPromptKind.Password when _password is not null && !PasswordDelivered && AskPassPrompt.IsPasswordFor(prompt, _userName, _host):
                await RespondAsync(pipe, AskPassProtocol.Answer, _password.Memory, cancellationToken).ConfigureAwait(false);
                PasswordDelivered = true;
                break;

            case AskPassPromptKind.HostKey:
                var trusted = await _hostKeys.ConfirmAsync(AskPassPrompt.ParseHostKey(prompt, _host), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await RespondAsync(pipe, AskPassProtocol.Answer, Encoding.ASCII.GetBytes(trusted ? "yes" : "no"), cancellationToken).ConfigureAwait(false);
                break;

            default:
                await RespondAsync(pipe, AskPassProtocol.Refuse, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private static async Task RespondAsync(Stream pipe, byte status, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        try
        {
            await pipe.WriteAsync(new[] { status }, cancellationToken).ConfigureAwait(false);
            await AskPassProtocol.WriteMessageAsync(pipe, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // L'helper ha chiuso prima della risposta.
        }
    }
}
