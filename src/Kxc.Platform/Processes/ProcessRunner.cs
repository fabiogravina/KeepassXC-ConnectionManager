using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Kxc.Core.Platform;

namespace Kxc.Platform.Processes;

/// <summary>
/// Avvio dei processi con argomenti in lista (nessuna shell), stdin e stdout come byte e
/// terminazione del processo alla scadenza o all'annullamento.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessOutcome> RunAsync(
        ProcessRequest request,
        ReadOnlyMemory<byte> standardInput,
        Func<Stream, CancellationToken, Task> readStandardOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(readStandardOutput);

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ProcessStartException(request.FileName, ex);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        await using var killOnCancel = timeout.Token.Register(() => Kill(process));

        try
        {
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            var stdout = process.StandardOutput.BaseStream;
            var stdoutTask = ReadAllAsync(stdout, readStandardOutput, timeout.Token);

            await WriteStandardInputAsync(process, standardInput, timeout.Token).ConfigureAwait(false);
            await stdoutTask.ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return new ProcessOutcome(process.ExitCode, stderr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Il processo {Path.GetFileName(request.FileName)} non ha risposto entro {request.Timeout.TotalSeconds:0} secondi.");
        }
    }

    private static async Task ReadAllAsync(Stream stdout, Func<Stream, CancellationToken, Task> reader, CancellationToken cancellationToken)
    {
        await reader(stdout, cancellationToken).ConfigureAwait(false);

        // Se il lettore si ferma prima della fine, il processo non deve restare bloccato su stdout pieno.
        await stdout.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteStandardInputAsync(Process process, ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
    {
        var stdin = process.StandardInput.BaseStream;
        try
        {
            if (!input.IsEmpty)
            {
                await stdin.WriteAsync(input, cancellationToken).ConfigureAwait(false);
                await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Il processo è uscito prima di leggere stdin (per esempio file mancante): conta l'exit code.
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // Pipe già chiusa dal processo.
            }
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Già terminato.
        }
        catch (Win32Exception)
        {
            // Già in chiusura.
        }
    }
}

public sealed class ProcessStartException : Exception
{
    public ProcessStartException()
    {
    }

    public ProcessStartException(string message)
        : base(message)
    {
    }

    public ProcessStartException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ProcessStartException(string fileName, Win32Exception innerException)
        : base($"Impossibile avviare {fileName}.", innerException)
    {
        FileName = fileName;
    }

    public string? FileName { get; }
}
