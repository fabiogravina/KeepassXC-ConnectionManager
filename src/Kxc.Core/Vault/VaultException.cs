namespace Kxc.Core.Vault;

/// <summary>Cause di errore che l'interfaccia traduce in messaggi con una proposta di soluzione.</summary>
public enum VaultError
{
    CliNotFound,
    CliVersionTooOld,
    DatabaseNotFound,
    KeyFileNotFound,
    UnsupportedFormat,

    /// <summary>Password errata o key file mancante: keepassxc-cli non li distingue.</summary>
    InvalidCredentials,

    EntryNotFound,
    Locked,
    Timeout,
    ReadFailed,
}

public sealed class VaultException : Exception
{
    public VaultException()
    {
    }

    public VaultException(string message)
        : base(message)
    {
    }

    public VaultException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public VaultException(VaultError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public VaultError Error { get; } = VaultError.ReadFailed;
}
