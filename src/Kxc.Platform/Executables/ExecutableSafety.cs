using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Kxc.Platform.Executables;

/// <summary>
/// Un eseguibile si usa solo se nessun altro utente può sostituirlo: altrimenti potrebbe ricevere
/// la password del database o delle voci (S8 dell'analisi di sicurezza).
/// </summary>
public static class ExecutableSafety
{
    public static bool IsSafe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return false;
        }

        var full = Path.GetFullPath(path);
        try
        {
            return OperatingSystem.IsWindows() ? IsSafeWindows(full) : IsSafeUnix(full);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
        {
            // Permessi non leggibili: nel dubbio l'eseguibile non si usa.
            return false;
        }
    }

    // ---------- Windows: lista di principali ammessi, proprietario, catena delle cartelle ----------

    /// <summary>Diritti che permettono di cambiare il contenuto del file o della cartella.</summary>
    [SupportedOSPlatform("windows")]
    private const FileSystemRights ContentWriteRights =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.DeleteSubdirectoriesAndFiles;

    /// <summary>Diritti che permettono di sostituire l'oggetto o di prenderne il controllo.</summary>
    [SupportedOSPlatform("windows")]
    private const FileSystemRights ControlRights =
        FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    [SupportedOSPlatform("windows")]
    private static bool IsSafeWindows(string file)
    {
        var trusted = TrustedPrincipals();
        if (!IsTrustedWindows(new FileInfo(file).GetAccessControl(), trusted, ContentWriteRights | ControlRights))
        {
            return false;
        }

        // Cartella del file: nessuno deve poter aggiungere, togliere o sostituire file.
        var directory = new DirectoryInfo(Path.GetDirectoryName(file)!);
        if (!IsTrustedWindows(directory.GetAccessControl(), trusted, ContentWriteRights | ControlRights))
        {
            return false;
        }

        // Cartelle superiori: nessuno deve poterle rinominare (Delete) o prenderne il controllo. La
        // creazione di sottocartelle è normale (per esempio in C:\) e non permette di sostituire
        // una cartella esistente.
        for (var parent = directory.Parent; parent is not null; parent = parent.Parent)
        {
            if (!IsTrustedWindows(parent.GetAccessControl(), trusted, ControlRights | FileSystemRights.DeleteSubdirectoriesAndFiles))
            {
                return false;
            }
        }

        return true;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsTrustedWindows(FileSystemSecurity security, HashSet<SecurityIdentifier> trusted, FileSystemRights dangerous)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && !trusted.Contains(owner))
        {
            return false;
        }

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow
                && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0
                && (rule.FileSystemRights & dangerous) != 0
                && rule.IdentityReference is SecurityIdentifier sid
                && !trusted.Contains(sid))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Utente corrente, SYSTEM, Administrators e TrustedInstaller.</summary>
    [SupportedOSPlatform("windows")]
    private static HashSet<SecurityIdentifier> TrustedPrincipals()
    {
        var trusted = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
            new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"), // NT SERVICE\TrustedInstaller
        };
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is { } user)
        {
            trusted.Add(user);
        }

        return trusted;
    }

    // ---------- Linux: permessi di gruppo e altri sul file risolto e sulle cartelle ----------

    [UnsupportedOSPlatform("windows")]
    private static bool IsSafeUnix(string file)
    {
        // Si controlla il file vero, non il collegamento simbolico (né la sola cartella del collegamento).
        var target = new FileInfo(file).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file;
        if (IsWritableByOthersUnix(target))
        {
            return false;
        }

        for (var directory = Path.GetDirectoryName(target); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            var mode = File.GetUnixFileMode(directory);
            var othersCanWrite = (mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0;

            // Con lo sticky bit (come /tmp) gli altri non possono rinominare o cancellare file altrui.
            if (othersCanWrite && (mode & UnixFileMode.StickyBit) == 0)
            {
                return false;
            }
        }

        // Limite noto: il proprietario (root o l'utente corrente) va controllato su Kubuntu, perché
        // .NET non espone l'uid del file senza codice nativo.
        return true;
    }

    [UnsupportedOSPlatform("windows")]
    private static bool IsWritableByOthersUnix(string path) =>
        (File.GetUnixFileMode(path) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0;
}
