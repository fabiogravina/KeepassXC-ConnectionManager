namespace Kxc.Core.Platform;

/// <summary>
/// Avvio di un client che resta aperto per conto suo (mstsc, terminale). Argomenti in lista,
/// mai segreti (SEC-01).
/// </summary>
public interface IProcessLauncher
{
    /// <param name="environment">Variabili aggiunte a quelle dell'app; mai valori segreti.</param>
    ILaunchedProcess Start(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null);
}

public interface ILaunchedProcess : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);
}

/// <summary>True se l'eseguibile si può usare: né lui né la sua cartella sono scrivibili da altri utenti.</summary>
public delegate bool ExecutableSafetyCheck(string path);

/// <summary>Credenziali del sistema usate dai client (su Windows, il Gestore credenziali).</summary>
public interface ISystemCredentialStore
{
    /// <summary>Credenziale valida solo per la sessione di accesso corrente, mai salvata su disco.</summary>
    void WriteSessionCredential(string target, string userName, ReadOnlySpan<byte> utf8Password);

    bool Delete(string target);
}

/// <summary>
/// Connessioni TCP stabilite verso una porta remota, per sapere quando un client si è collegato.
/// Non risolve nomi: l'app non fa richieste di rete per conto suo (SEC-06).
/// </summary>
public interface ITcpConnectionProbe
{
    IReadOnlySet<string> EstablishedEndpoints(int remotePort);
}
