using System.Buffers.Binary;

namespace Kxc.Core.Connections;

/// <summary>
/// Protocollo tra kxc-askpass e l'app, su un named pipe (socket Unix su Linux) riservato all'utente.
/// Richiesta: lunghezza (int32) + domanda di ssh in UTF-8. Risposta: esito (byte) + lunghezza
/// (int32) + risposta in UTF-8. Condiviso dall'helper e dall'app.
/// </summary>
public static class AskPassProtocol
{
    /// <summary>Variabile d'ambiente con il nome del canale; il nome non è un segreto.</summary>
    public const string ChannelVariable = "KXC_ASKPASS_CHANNEL";

    public const byte Answer = 0;
    public const byte Refuse = 1;

    public const int MaxMessageBytes = 64 * 1024;

    public static async Task WriteMessageAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Legge un messaggio nel buffer indicato; restituisce i byte letti.</summary>
    public static async Task<int> ReadMessageAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > buffer.Length)
        {
            throw new InvalidDataException("Messaggio askpass non valido.");
        }

        await stream.ReadExactlyAsync(buffer[..length], cancellationToken).ConfigureAwait(false);
        return length;
    }
}
