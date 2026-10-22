namespace Kxc.Core.Connections;

/// <summary>
/// Prima connessione SSH a un host sconosciuto: l'utente vede l'impronta della chiave e decide
/// se fidarsi (decisione del 21 ottobre 2026, soluzione «a»).
/// </summary>
public interface IHostKeyConfirmation
{
    Task<bool> ConfirmAsync(HostKeyQuestion question, CancellationToken cancellationToken);
}

/// <summary>Dati estratti dalla domanda di ssh; <see cref="Fingerprint"/> è null se non riconosciuta.</summary>
public sealed record HostKeyQuestion(string Host, string? KeyType, string? Fingerprint, string RawPrompt);
