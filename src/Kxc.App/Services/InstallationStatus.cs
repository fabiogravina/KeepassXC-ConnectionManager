using Kxc.Core.Platform;

namespace Kxc.App.Services;

/// <summary>
/// S5: se la cartella dell'app (o kxc-askpass) è modificabile da altri utenti del computer, uno di
/// loro potrebbe sostituirla e leggere le password. L'app lo dice prima dello sblocco.
/// </summary>
public sealed record InstallationStatus(bool IsSafe, IReadOnlyList<string> UnsafePaths)
{
    public static InstallationStatus Safe { get; } = new(true, []);

    public static InstallationStatus Check(ExecutableSafetyCheck isSafe, IEnumerable<string?> executables)
    {
        ArgumentNullException.ThrowIfNull(isSafe);
        ArgumentNullException.ThrowIfNull(executables);

        var unsafePaths = executables
            .OfType<string>()
            .Where(File.Exists)
            .Where(path => !isSafe(path))
            .ToList();
        return new InstallationStatus(unsafePaths.Count == 0, unsafePaths);
    }

    /// <summary>Eseguibile dell'app e helper di SSH_ASKPASS accanto a lui.</summary>
    public static IEnumerable<string?> AppExecutables()
    {
        yield return Environment.ProcessPath;
        yield return Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "kxc-askpass.exe" : "kxc-askpass");
    }
}
