namespace Kxc.App.Services;

/// <summary>
/// S13: un database o un key file su un percorso di rete fa sì che Windows si autentichi su quel
/// server con le credenziali dell'utente (NTLM). Non è vietato (un NAS è un uso normale), ma lo si dice.
/// </summary>
public static class NetworkPaths
{
    public static bool IsNetworkPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !OperatingSystem.IsWindows())
        {
            return false;
        }

        // \\server\condivisione, \\?\UNC\server\condivisione
        if (path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\C", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
