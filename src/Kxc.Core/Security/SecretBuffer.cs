using System.Security.Cryptography;
using System.Text;

namespace Kxc.Core.Security;

/// <summary>
/// Segreto in un array di byte bloccato in memoria (il GC non lo copia altrove) e azzerato al
/// Dispose (SEC-02). Non espone mai il contenuto come stringa.
/// </summary>
public sealed class SecretBuffer : IDisposable
{
    private readonly byte[] _buffer;
    private bool _disposed;

    private SecretBuffer(int length)
    {
        _buffer = GC.AllocateArray<byte>(length, pinned: true);
    }

    public int Length => _buffer.Length;

    public bool IsEmpty => _buffer.Length == 0;

    public ReadOnlySpan<byte> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffer;
        }
    }

    public ReadOnlyMemory<byte> Memory
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffer;
        }
    }

    /// <summary>Codifica UTF-8 dei caratteri, senza passare da una stringa intermedia.</summary>
    public static SecretBuffer FromChars(ReadOnlySpan<char> chars)
    {
        var secret = new SecretBuffer(Encoding.UTF8.GetByteCount(chars));
        Encoding.UTF8.GetBytes(chars, secret._buffer);
        return secret;
    }

    public static SecretBuffer FromBytes(ReadOnlySpan<byte> bytes)
    {
        var secret = new SecretBuffer(bytes.Length);
        bytes.CopyTo(secret._buffer);
        return secret;
    }

    /// <summary>Copia con un a capo finale, come la vuole keepassxc-cli su stdin.</summary>
    public SecretBuffer WithTrailingNewline()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var copy = new SecretBuffer(_buffer.Length + 1);
        _buffer.CopyTo(copy._buffer, 0);
        copy._buffer[^1] = (byte)'\n';
        return copy;
    }

    /// <summary>Confronto a tempo costante, usato dai test e dai controlli di sicurezza.</summary>
    public bool ContentEquals(ReadOnlySpan<byte> other)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return CryptographicOperations.FixedTimeEquals(_buffer, other);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_buffer);
            _disposed = true;
        }
    }
}
