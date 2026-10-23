using System.Diagnostics;

namespace Kxc.Tests.Connectors;

/// <summary>
/// S17: OpenSSH per Windows deve avviare SSH_ASKPASS con il percorso tra virgolette anche se
/// contiene spazi (l'app può stare in una cartella come «C:\Program Files\...»). Con il percorso
/// senza virgolette Windows proverebbe prima «...\a.exe»: qui quel file esiste ed è un'esca.
///
/// Nessun binario nuovo (Smart App Control li blocca): l'helper è una copia firmata di cmd.exe,
/// che senza input stampa sempre la stessa prima riga, e la chiave è cifrata con quella riga;
/// l'esca è una copia di whoami.exe, che con un argomento sconosciuto non stampa nulla su stdout.
/// La chiave si legge solo se è partito l'helper giusto.
/// </summary>
public sealed class OpenSshAskPassPathTests : IDisposable
{
    private static readonly string OpenSsh = Path.Combine(Environment.SystemDirectory, "OpenSSH");
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kxc-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task S17_askpass_in_a_folder_with_spaces_is_started_with_quotes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "OpenSSH per Windows: solo Windows");
        var keygen = Path.Combine(OpenSsh, "ssh-keygen.exe");
        Assert.SkipUnless(File.Exists(keygen), "OpenSSH per Windows non installato");

        var spaced = Path.Combine(_directory, "a b");
        Directory.CreateDirectory(spaced);
        var helper = Path.Combine(spaced, "askpass.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), helper);
        File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), Path.Combine(_directory, "a.exe"));

        var (_, banner) = await RunAsync(helper, ["Domanda di prova"]);
        var passphrase = banner.Split(["\r\n", "\n"], StringSplitOptions.None)[0];
        Assert.NotEmpty(passphrase);

        var key = Path.Combine(_directory, "chiave");
        var (created, _) = await RunAsync(keygen, ["-q", "-t", "ed25519", "-N", passphrase, "-C", "prova", "-f", key]);
        Assert.Equal(0, created);
        File.Delete(key + ".pub");

        var askPass = new Dictionary<string, string> { ["SSH_ASKPASS"] = helper, ["SSH_ASKPASS_REQUIRE"] = "force" };
        var (exitCode, output) = await RunAsync(keygen, ["-y", "-f", key], askPass);

        Assert.Equal(0, exitCode);
        Assert.StartsWith("ssh-ed25519 ", output, StringComparison.Ordinal);

        // Controprova: se rispondesse l'esca, la chiave non si leggerebbe.
        askPass["SSH_ASKPASS"] = Path.Combine(_directory, "a.exe");
        var (decoyExitCode, _) = await RunAsync(keygen, ["-y", "-f", key], askPass);
        Assert.NotEqual(0, decoyExitCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string file, string[] arguments, Dictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? [])
        {
            info.Environment[name] = value;
        }

        using var process = Process.Start(info)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30));
        await error;
        return (process.ExitCode, await output);
    }
}
