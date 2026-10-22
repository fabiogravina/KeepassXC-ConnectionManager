namespace Kxc.Core.Vault;

/// <summary>Osserva il file del database e segnala quando è cambiato ed è stabile (FR-11).</summary>
public interface IDatabaseWatcher : IDisposable
{
    event EventHandler? Changed;

    /// <summary>Inizia a osservare il file; lo stato attuale è quello già letto.</summary>
    void Watch(string databasePath);

    void StopWatching();
}
