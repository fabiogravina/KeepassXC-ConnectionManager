namespace Kxc.Core.Platform;

/// <summary>
/// Processo da avviare: gli argomenti sono una lista, mai una stringa interpretata da una shell,
/// e non contengono mai segreti (SEC-01).
/// </summary>
public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments)
{
    public string? WorkingDirectory { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record ProcessOutcome(int ExitCode, string StandardError);

public interface IProcessRunner
{
    /// <summary>
    /// Avvia il processo, scrive <paramref name="standardInput"/> su stdin e lo chiude, e passa
    /// stdout a <paramref name="readStandardOutput"/> come flusso di byte (mai come stringa, perché
    /// può contenere segreti). Alla scadenza o all'annullamento il processo viene terminato.
    /// </summary>
    Task<ProcessOutcome> RunAsync(
        ProcessRequest request,
        ReadOnlyMemory<byte> standardInput,
        Func<Stream, CancellationToken, Task> readStandardOutput,
        CancellationToken cancellationToken);
}
