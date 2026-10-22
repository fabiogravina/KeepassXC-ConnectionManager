namespace Kxc.Core.Security;

/// <summary>
/// Credenziali di sblocco: percorso del database, password (facoltativa) e key file (facoltativo).
/// Chi le riceve ne diventa proprietario e le azzera con Dispose al blocco.
/// </summary>
public sealed class UnlockCredentials : IDisposable
{
    public UnlockCredentials(string databasePath, SecretBuffer? password, string? keyFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        DatabasePath = databasePath;
        Password = password is { IsEmpty: false } ? password : null;
        KeyFilePath = string.IsNullOrWhiteSpace(keyFilePath) ? null : keyFilePath;

        if (password is { IsEmpty: true })
        {
            password.Dispose();
        }

        if (Password is null && KeyFilePath is null)
        {
            throw new ArgumentException("Serve almeno una password o un key file.", nameof(password));
        }
    }

    public string DatabasePath { get; }

    public SecretBuffer? Password { get; }

    public string? KeyFilePath { get; }

    public void Dispose() => Password?.Dispose();
}
