using System.Buffers.Binary;

namespace Kxc.Vault;

public enum KdbxFormatKind
{
    Unknown,

    /// <summary>KeePass 1.x (.kdb): non supportato da keepassxc-cli per la lettura diretta.</summary>
    Kdb1,

    Kdbx3,
    Kdbx4,
}

/// <summary>Riconosce il formato dalle firme nell'intestazione, senza decifrare nulla.</summary>
public static class KdbxFormat
{
    private const uint Signature1 = 0x9AA2D903;
    private const uint SignatureKdbx = 0xB54BFB67;
    private const uint SignatureKdb1 = 0xB54BFB65;

    public static KdbxFormatKind Detect(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                return KdbxFormatKind.Unknown;
            }
        }

        return Detect(header);
    }

    public static KdbxFormatKind Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(header) != Signature1)
        {
            return KdbxFormatKind.Unknown;
        }

        var signature2 = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (signature2 == SignatureKdb1)
        {
            return KdbxFormatKind.Kdb1;
        }

        if (signature2 != SignatureKdbx)
        {
            return KdbxFormatKind.Unknown;
        }

        var major = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
        return major switch
        {
            3 => KdbxFormatKind.Kdbx3,
            4 => KdbxFormatKind.Kdbx4,
            _ => KdbxFormatKind.Unknown,
        };
    }

    public static bool IsSupported(KdbxFormatKind kind) => kind is KdbxFormatKind.Kdbx3 or KdbxFormatKind.Kdbx4;
}
