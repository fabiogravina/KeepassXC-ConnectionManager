using System.Xml;

namespace Kxc.Vault;

/// <summary>
/// Flusso in sola lettura tra stdout di "keepassxc-cli export" e XmlReader (S6): sostituisce con
/// spazi, nello stesso buffer in cui arrivano, i byte di testo dei valori protetti
/// (<c>ProtectInMemory="True"</c>), di ogni valore della chiave <c>Password</c> e di tutto
/// <c>&lt;Binaries&gt;</c> (gli allegati). Il parser e i suoi buffer non vedono mai i segreti, e
/// l'XML resta valido perché solo il testo tra i tag diventa spazi.
/// </summary>
internal sealed class ProtectedValueFilterStream : Stream
{
    /// <summary>I tag di KeePassXC sono brevi: oltre questa lunghezza l'input non è un export valido.</summary>
    private const int MaxTagBytes = 8 * 1024;

    private const int MaxKeyBytes = 256;

    private readonly Stream _inner;
    private readonly byte[] _tag = new byte[MaxTagBytes];
    private readonly byte[] _key = new byte[MaxKeyBytes];
    private int _tagLength;
    private int _keyLength;
    private bool _inTag;
    private bool _capturingKey;
    private bool _secretText;
    private bool _inBinaries;
    private bool _lastKeyIsPassword;

    public ProtectedValueFilterStream(Stream inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer);
        Filter(buffer[..read]);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Filter(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void Filter(Span<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            if (_inTag)
            {
                if (_tagLength == MaxTagBytes)
                {
                    throw new XmlException("Tag troppo lungo nell'export di keepassxc-cli.");
                }

                _tag[_tagLength++] = b;
                if (b == (byte)'>')
                {
                    _inTag = false;
                    OnTag(_tag.AsSpan(0, _tagLength));
                }

                continue;
            }

            if (b == (byte)'<')
            {
                _inTag = true;
                _tagLength = 1;
                _tag[0] = b;
                continue;
            }

            if (_secretText || _inBinaries)
            {
                data[i] = (byte)' ';
            }
            else if (_capturingKey && _keyLength < MaxKeyBytes)
            {
                _key[_keyLength++] = b;
            }
        }
    }

    /// <summary>Tag completo, da '&lt;' a '&gt;' compresi.</summary>
    private void OnTag(ReadOnlySpan<byte> tag)
    {
        var selfClosing = tag.Length >= 2 && tag[^2] == (byte)'/';
        var closing = tag.Length >= 2 && tag[1] == (byte)'/';
        var name = Name(tag, closing);

        if (closing)
        {
            if (name.SequenceEqual("Key"u8) && _capturingKey)
            {
                _capturingKey = false;
                _lastKeyIsPassword = _key.AsSpan(0, _keyLength).SequenceEqual("Password"u8);
            }
            else if (name.SequenceEqual("Value"u8))
            {
                _secretText = false;
            }
            else if (name.SequenceEqual("Binaries"u8))
            {
                _inBinaries = false;
            }

            return;
        }

        if (name.SequenceEqual("Key"u8) && !selfClosing)
        {
            _capturingKey = true;
            _keyLength = 0;
        }
        else if (name.SequenceEqual("Value"u8) && !selfClosing)
        {
            _secretText = _lastKeyIsPassword || tag.IndexOf("ProtectInMemory=\"True\""u8) >= 0;
        }
        else if (name.SequenceEqual("Binaries"u8) && !selfClosing)
        {
            _inBinaries = true;
        }
        else if (name.SequenceEqual("String"u8))
        {
            _lastKeyIsPassword = false;
        }
    }

    private static ReadOnlySpan<byte> Name(ReadOnlySpan<byte> tag, bool closing)
    {
        var start = closing ? 2 : 1;
        var end = start;
        while (end < tag.Length && tag[end] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'/' or (byte)'>'))
        {
            end++;
        }

        return tag[start..end];
    }
}
