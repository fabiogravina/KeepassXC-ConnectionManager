using Kxc.Core.Entries;
using Kxc.Core.Security;

namespace Kxc.Core.Vault;

/// <summary>Accesso in sola lettura al database KeePassXC (SEC-04).</summary>
public interface IVaultService
{
    bool IsUnlocked { get; }

    /// <summary>
    /// Sblocca e legge le voci. In caso di successo il servizio diventa proprietario delle
    /// credenziali; in caso di errore le azzera.
    /// </summary>
    Task<VaultSnapshot> UnlockAsync(UnlockCredentials credentials, CancellationToken cancellationToken);

    /// <summary>Rilegge il database con le credenziali già in memoria (FR-11).</summary>
    Task<VaultSnapshot> ReloadAsync(CancellationToken cancellationToken);

    /// <summary>Legge la password della voce al momento della connessione; il chiamante la azzera.</summary>
    Task<SecretBuffer> ReadPasswordAsync(ConnectionEntry entry, CancellationToken cancellationToken);

    /// <summary>Azzera le credenziali in memoria.</summary>
    void Lock();
}

public sealed record VaultSnapshot(string DatabaseName, IReadOnlyList<VaultEntry> Entries);

/// <summary>Percorso di keepassxc-cli scelto nelle impostazioni o rilevato.</summary>
public interface IKeePassXcCliLocator
{
    string? FindCli();
}
