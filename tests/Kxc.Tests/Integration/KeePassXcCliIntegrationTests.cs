using Kxc.Core.Entries;
using Kxc.Core.Vault;
using Kxc.Platform.Executables;
using Kxc.Platform.Processes;
using Kxc.Tests.TestData;
using Kxc.Vault;

namespace Kxc.Tests.Integration;

/// <summary>
/// Prove con keepassxc-cli vero sulla fixture. Saltate se keepassxc-cli non è installato.
/// </summary>
public class KeePassXcCliIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    private static KeePassXcVaultService RealVault()
    {
        var locator = new KeePassXcCliLocator(() => null);
        Assert.SkipWhen(locator.FindCli() is null, "keepassxc-cli non installato");
        return new KeePassXcVaultService(new ProcessRunner(), locator);
    }

    [Fact]
    public async Task Unlocks_the_fixture_and_builds_the_list()
    {
        using var vault = RealVault();

        var snapshot = await vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken);
        var entries = ConnectionCatalog.Build(snapshot.Entries, Now);

        Assert.Equal("Fixture KXC", snapshot.DatabaseName);
        Assert.Equal(
            ["Contabilità", "Gateway", "NAS", "Router", "Senza host", "Server file", "Titolo \"strano\" / con slash"],
            entries.Select(e => e.Title).Order(StringComparer.Ordinal));

        var router = entries.Single(e => e.Title == "Router");
        Assert.Equal(ConnectionProtocols.Rdp | ConnectionProtocols.Ssh, router.Protocols);
        Assert.Equal(new Guid("00000000-0000-0000-0000-000000000401"), router.Uuid);
        Assert.Equal(EntryWarning.MissingHost, entries.Single(e => e.Title == "Senza host").Warning);
        Assert.Equal(EntryWarning.InvalidConnectionData, entries.Single(e => e.Title.StartsWith("Titolo", StringComparison.Ordinal)).Warning);
    }

    [Fact]
    public async Task Reads_a_password_with_special_characters_and_unicode_path()
    {
        using var vault = RealVault();
        var snapshot = await vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken);
        var contabilita = ConnectionCatalog.Build(snapshot.Entries, Now).Single(e => e.Title == "Contabilità");

        using var password = await vault.ReadPasswordAsync(contabilita, TestContext.Current.CancellationToken);

        Assert.True(password.ContentEquals(Fixture.Utf8("p@ss \"w0rd\" ;\\ è€ ' $HOME %PATH%")));
    }

    [Fact]
    public async Task Wrong_password_is_invalid_credentials()
    {
        using var vault = RealVault();

        var error = await Assert.ThrowsAsync<VaultException>(() =>
            vault.UnlockAsync(Fixture.Credentials("sbagliata"), TestContext.Current.CancellationToken));

        Assert.Equal(VaultError.InvalidCredentials, error.Error);
    }

    [Fact]
    public async Task Missing_key_file_argument_is_invalid_credentials()
    {
        using var vault = RealVault();

        var error = await Assert.ThrowsAsync<VaultException>(() =>
            vault.UnlockAsync(new Kxc.Core.Security.UnlockCredentials(Fixture.DatabasePath, Kxc.Core.Security.SecretBuffer.FromChars("test"), null), TestContext.Current.CancellationToken));

        Assert.Equal(VaultError.InvalidCredentials, error.Error);
    }

    [Fact]
    public async Task Unlocks_a_kdbx4_argon2_database()
    {
        Assert.SkipUnless(File.Exists(Fixture.Kdbx4DatabasePath), Fixture.Kdbx4MissingReason);
        using var vault = RealVault();

        var snapshot = await vault.UnlockAsync(
            new Kxc.Core.Security.UnlockCredentials(Fixture.Kdbx4DatabasePath, Kxc.Core.Security.SecretBuffer.FromChars(Fixture.DatabasePassword), Fixture.KeyFilePath),
            TestContext.Current.CancellationToken);

        Assert.Equal("Prova", Assert.Single(snapshot.Entries).Title);
    }

    [Fact]
    public async Task Reload_uses_the_credentials_in_memory()
    {
        using var vault = RealVault();
        await vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken);

        var snapshot = await vault.ReloadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(9, snapshot.Entries.Count);
    }
}
