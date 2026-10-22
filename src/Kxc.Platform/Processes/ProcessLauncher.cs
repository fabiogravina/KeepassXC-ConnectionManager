using System.ComponentModel;
using System.Diagnostics;
using Kxc.Core.Platform;

namespace Kxc.Platform.Processes;

/// <summary>Avvia un client grafico o un terminale senza shell e senza redirezioni.</summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    public ILaunchedProcess Start(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        try
        {
            return new LaunchedProcess(Process.Start(startInfo) ?? throw new ProcessStartException($"Impossibile avviare {fileName}."));
        }
        catch (Win32Exception ex)
        {
            throw new ProcessStartException(fileName, ex);
        }
    }

    private sealed class LaunchedProcess(Process process) : ILaunchedProcess
    {
        public int Id => process.Id;

        public bool HasExited => process.HasExited;

        public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);

        public void Dispose() => process.Dispose();
    }
}
