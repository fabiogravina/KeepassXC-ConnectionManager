using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Kxc.Core.Platform;

namespace Kxc.Platform.Credentials;

/// <summary>
/// Gestore credenziali di Windows tramite CredWrite: nessun processo e nessun argomento con la
/// password (al contrario di cmdkey). Persistenza solo per la sessione di accesso (fase 0, RDP).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsCredentialStore : ISystemCredentialStore
{
    private const int CredTypeDomainPassword = 2;
    private const int CredPersistSession = 1;

    public unsafe void WriteSessionCredential(string target, string userName, ReadOnlySpan<byte> utf8Password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        // Il blob è la password in UTF-16, in un buffer bloccato e azzerato subito dopo.
        var length = Encoding.UTF8.GetCharCount(utf8Password);
        var blob = GC.AllocateArray<char>(length, pinned: true);
        var targetPtr = Marshal.StringToHGlobalUni(target);
        var userPtr = Marshal.StringToHGlobalUni(userName);
        try
        {
            Encoding.UTF8.GetChars(utf8Password, blob);
            fixed (char* password = blob)
            {
                var credential = new NativeCredential
                {
                    Type = CredTypeDomainPassword,
                    TargetName = targetPtr,
                    CredentialBlobSize = length * sizeof(char),
                    CredentialBlob = (nint)password,
                    Persist = CredPersistSession,
                    UserName = userPtr,
                };

                if (!CredWriteW(ref credential, 0))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
            }
        }
        finally
        {
            Array.Clear(blob);
            Marshal.FreeHGlobal(targetPtr);
            Marshal.FreeHGlobal(userPtr);
        }
    }

    public bool Delete(string target) => CredDeleteW(target, CredTypeDomainPassword, 0);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public nint TargetName;
        public nint Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public nint CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWriteW(ref NativeCredential credential, int flags);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDeleteW(string target, int type, int flags);
}
