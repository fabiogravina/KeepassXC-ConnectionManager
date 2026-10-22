using Kxc.Core.Vault;

namespace Kxc.Platform.Executables;

/// <summary>
/// Trova keepassxc-cli: prima il percorso scelto nelle impostazioni, poi i percorsi standard di
/// installazione, infine il PATH. Scarta gli eseguibili in cartelle scrivibili da altri utenti.
/// </summary>
public sealed class KeePassXcCliLocator : IKeePassXcCliLocator
{
    private readonly Func<string?> _configuredPath;
    private readonly Func<string, bool> _isSafe;

    public KeePassXcCliLocator(Func<string?> configuredPath)
        : this(configuredPath, ExecutableSafety.IsSafe)
    {
    }

    internal KeePassXcCliLocator(Func<string?> configuredPath, Func<string, bool> isSafe)
    {
        ArgumentNullException.ThrowIfNull(configuredPath);
        ArgumentNullException.ThrowIfNull(isSafe);
        _configuredPath = configuredPath;
        _isSafe = isSafe;
    }

    public string? FindCli()
    {
        var configured = _configuredPath();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            // Un percorso scelto dall'utente non viene sostituito in silenzio con un altro.
            return _isSafe(configured) ? configured : null;
        }

        return Candidates().FirstOrDefault(_isSafe);
    }

    public static IEnumerable<string> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     })
            {
                if (!string.IsNullOrEmpty(root))
                {
                    yield return Path.Combine(root, "KeePassXC", "keepassxc-cli.exe");
                }
            }
        }
        else
        {
            yield return "/usr/bin/keepassxc-cli";
            yield return "/usr/local/bin/keepassxc-cli";
            yield return "/snap/bin/keepassxc.cli";
        }

        var executable = OperatingSystem.IsWindows() ? "keepassxc-cli.exe" : "keepassxc-cli";
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Path.IsPathRooted(directory))
            {
                yield return Path.Combine(directory, executable);
            }
        }
    }
}
