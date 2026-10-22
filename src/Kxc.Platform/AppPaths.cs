namespace Kxc.Platform;

/// <summary>
/// Cartella di configurazione separata per sistema operativo: %APPDATA% su Windows,
/// $XDG_CONFIG_HOME (o ~/.config) su Linux.
/// </summary>
public sealed class AppPaths
{
    private const string WindowsFolderName = "KXC-ConnectionManager";
    private const string UnixFolderName = "kxc-connectionmanager";

    public AppPaths()
        : this(DefaultConfigDirectory())
    {
    }

    public AppPaths(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        ConfigDirectory = configDirectory;
    }

    public string ConfigDirectory { get; }

    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    /// <summary>Crea la cartella leggibile solo dall'utente.</summary>
    public void EnsureConfigDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            // %APPDATA% è già privato dell'utente.
            Directory.CreateDirectory(ConfigDirectory);
        }
        else
        {
            Directory.CreateDirectory(ConfigDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static string DefaultConfigDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), WindowsFolderName);
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var root = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, UnixFolderName);
    }
}
