using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Kxc.Platform.Executables;

/// <summary>
/// Un eseguibile si usa solo se né il file né la sua cartella sono scrivibili da altri utenti:
/// altrimenti qualcuno potrebbe sostituirlo e ricevere la password del database.
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

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is null)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return !IsWritableByOthersWindows(new FileInfo(path))
                && !IsWritableByOthersWindows(new DirectoryInfo(directory));
        }

        return !IsWritableByOthersUnix(path) && !IsWritableByOthersUnix(directory);
    }

    [UnsupportedOSPlatform("windows")]
    private static bool IsWritableByOthersUnix(string path)
    {
        const UnixFileMode othersWrite = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
        return (File.GetUnixFileMode(path) & othersWrite) != 0;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWritableByOthersWindows(FileSystemInfo item)
    {
        FileSystemSecurity security = item is FileInfo file
            ? file.GetAccessControl()
            : ((DirectoryInfo)item).GetAccessControl();

        var broadGroups = new[]
        {
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
        };
        const FileSystemRights writeRights =
            FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow
                && broadGroups.Contains(rule.IdentityReference)
                && (rule.FileSystemRights & writeRights) != 0
                && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
