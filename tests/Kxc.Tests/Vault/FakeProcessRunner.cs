using System.Text;
using Kxc.Core.Platform;

namespace Kxc.Tests.Vault;

/// <summary>Processo simulato: registra argomenti e stdin e risponde con un output prestabilito.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<ProcessRequest, (int ExitCode, string StdOut, string StdErr)> _respond;

    public FakeProcessRunner(Func<ProcessRequest, (int ExitCode, string StdOut, string StdErr)> respond)
    {
        _respond = respond;
    }

    public List<(ProcessRequest Request, byte[] StandardInput)> Calls { get; } = [];

    public async Task<ProcessOutcome> RunAsync(
        ProcessRequest request,
        ReadOnlyMemory<byte> standardInput,
        Func<Stream, CancellationToken, Task> readStandardOutput,
        CancellationToken cancellationToken)
    {
        Calls.Add((request, standardInput.ToArray()));
        var (exitCode, stdout, stderr) = _respond(request);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(stdout));
        await readStandardOutput(stream, cancellationToken);
        return new ProcessOutcome(exitCode, stderr);
    }

    /// <summary>Risposte tipiche di keepassxc-cli 2.7.12.</summary>
    public static FakeProcessRunner KeePassXc(string exportXml, string password = "segreto", int exportExitCode = 0) => new(request =>
        request.Arguments[0] switch
        {
            "--version" => (0, "2.7.12\n", string.Empty),
            "export" => exportExitCode == 0 ? (0, exportXml, string.Empty) : (exportExitCode, string.Empty, string.Empty),
            "show" => (0, password + "\n", string.Empty),
            _ => (2, string.Empty, "comando inatteso"),
        });
}
