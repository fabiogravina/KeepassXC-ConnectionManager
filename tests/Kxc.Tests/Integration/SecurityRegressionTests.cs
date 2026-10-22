using Kxc.Core.Entries;
using Kxc.Core.Security;
using Kxc.Core.Vault;
using Kxc.Platform.Executables;
using Kxc.Platform.Processes;
using Kxc.Tests.TestData;
using Kxc.Vault;

namespace Kxc.Tests.Integration;

/// <summary>
/// Riproducono con keepassxc-cli vero le prove dell'analisi di sicurezza del 22 ottobre 2026
/// (Docs/Sicurezza - analisi e piano.md). Saltati se keepassxc-cli non è installato.
/// </summary>
public class SecurityRegressionTests
{
    private static readonly Guid ValidServer = new("00000000-0000-0000-0000-000000000702");

    private static async Task<(KeePassXcVaultService Vault, IReadOnlyList<ConnectionEntry> Entries)> UnlockEdgeCasesAsync()
    {
        var locator = new KeePassXcCliLocator(() => null);
        Assert.SkipWhen(locator.FindCli() is null, "keepassxc-cli non installato");

        var vault = new KeePassXcVaultService(new ProcessRunner(), locator);
        var snapshot = await vault.UnlockAsync(
            new UnlockCredentials(Fixture.EdgeCasesDatabasePath, SecretBuffer.FromChars(Fixture.DatabasePassword), Fixture.KeyFilePath),
            TestContext.Current.CancellationToken);
        return (vault, ConnectionCatalog.Build(snapshot.Entries, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task S2_valid_entry_sharing_the_path_of_an_expired_one_cannot_connect()
    {
        var (vault, entries) = await UnlockEdgeCasesAsync();
        using var _ = vault;

        var server = Assert.Single(entries, e => e.Title == "Server");
        Assert.Equal(ValidServer, server.Uuid);
        Assert.Equal(EntryWarning.AmbiguousPath, server.Warning);
    }

    [Fact]
    public async Task S2_password_of_the_expired_entry_is_never_returned_for_the_valid_one()
    {
        var (vault, entries) = await UnlockEdgeCasesAsync();
        using var _ = vault;
        var server = Assert.Single(entries, e => e.Title == "Server");

        // Anche ignorando l'avviso del catalogo, la verifica dell'UUID ferma la lettura.
        var error = await Assert.ThrowsAsync<VaultException>(() =>
            vault.ReadPasswordAsync(server with { Warning = EntryWarning.None }, TestContext.Current.CancellationToken));

        Assert.Equal(VaultError.EntryMismatch, error.Error);
    }

    [Fact]
    public async Task S9_entry_titled_like_an_option_is_read_correctly()
    {
        var (vault, entries) = await UnlockEdgeCasesAsync();
        using var _ = vault;
        var dashed = Assert.Single(entries, e => e.Title == "--all");

        using var password = await vault.ReadPasswordAsync(dashed, TestContext.Current.CancellationToken);

        Assert.True(password.ContentEquals(Fixture.Utf8("pw-trattino")));
    }
}
