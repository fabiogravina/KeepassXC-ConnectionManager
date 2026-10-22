using Kxc.Core.Entries;
using Kxc.Core.Security;
using Kxc.Core.Vault;
using Kxc.Tests.TestData;
using Kxc.Vault;

namespace Kxc.Tests.Vault;

public class KeePassXcVaultServiceTests
{
    private const string Cli = "keepassxc-cli";

    private static readonly string[] ReadCommands = ["--version", "export", "show"];

    private static readonly string ExportXml = File.ReadAllText(Fixture.XmlPath);

    private sealed class FixedLocator(string? path) : IKeePassXcCliLocator
    {
        public string? FindCli() => path;
    }

    private static KeePassXcVaultService Service(FakeProcessRunner runner, string? cli = Cli) => new(runner, new FixedLocator(cli));

    private static readonly ConnectionEntry Gateway = new()
    {
        Uuid = Guid.NewGuid(),
        Title = "Gateway",
        EntryPath = "Clienti/Rossi Srl/Gateway",
        Protocols = ConnectionProtocols.Ssh,
        Host = "gw.example",
    };

    [Fact]
    public async Task Database_password_goes_only_to_stdin()
    {
        var runner = FakeProcessRunner.KeePassXc(ExportXml);
        using var vault = Service(runner);

        await vault.UnlockAsync(Fixture.Credentials("pässword"), TestContext.Current.CancellationToken);

        var export = runner.Calls.Single(c => c.Request.Arguments[0] == "export");
        Assert.Equal(Fixture.Utf8("pässword\n"), export.StandardInput);
        Assert.DoesNotContain(export.Request.Arguments, a => a.Contains("pässword", StringComparison.Ordinal));
        Assert.Equal(["export", "-q", "-k", Fixture.KeyFilePath, "-f", "xml", "--", Fixture.DatabasePath], export.Request.Arguments);
        Assert.True(vault.IsUnlocked);
    }

    [Fact]
    public async Task Key_file_only_uses_no_password_and_empty_stdin()
    {
        var runner = FakeProcessRunner.KeePassXc(ExportXml);
        using var vault = Service(runner);

        await vault.UnlockAsync(new UnlockCredentials(Fixture.DatabasePath, null, Fixture.KeyFilePath), TestContext.Current.CancellationToken);

        var export = runner.Calls.Single(c => c.Request.Arguments[0] == "export");
        Assert.Contains("--no-password", export.Request.Arguments);
        Assert.Empty(export.StandardInput);
    }

    [Fact]
    public async Task Wrong_credentials_are_reported_and_zeroed()
    {
        var runner = FakeProcessRunner.KeePassXc(ExportXml, exportExitCode: 1);
        using var vault = Service(runner);
        var credentials = Fixture.Credentials("sbagliata");

        var error = await Assert.ThrowsAsync<VaultException>(() => vault.UnlockAsync(credentials, TestContext.Current.CancellationToken));

        Assert.Equal(VaultError.InvalidCredentials, error.Error);
        Assert.False(vault.IsUnlocked);
        Assert.Throws<ObjectDisposedException>(() => credentials.Password!.Span.Length);
    }

    [Fact]
    public async Task Missing_cli_is_reported()
    {
        using var vault = Service(FakeProcessRunner.KeePassXc(ExportXml), cli: null);

        var error = await Assert.ThrowsAsync<VaultException>(() => vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken));

        Assert.Equal(VaultError.CliNotFound, error.Error);
    }

    [Fact]
    public async Task Cli_older_than_2_7_0_is_refused()
    {
        var runner = new FakeProcessRunner(_ => (0, "2.6.6\n", string.Empty));
        using var vault = Service(runner);

        var error = await Assert.ThrowsAsync<VaultException>(() => vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken));

        Assert.Equal(VaultError.CliVersionTooOld, error.Error);
        Assert.DoesNotContain(runner.Calls, c => c.Request.Arguments[0] == "export");
    }

    [Fact]
    public async Task Missing_database_and_key_file_are_reported_before_running_the_cli()
    {
        var runner = FakeProcessRunner.KeePassXc(ExportXml);
        using var vault = Service(runner);

        var noDb = await Assert.ThrowsAsync<VaultException>(() =>
            vault.UnlockAsync(new UnlockCredentials("manca.kdbx", SecretBuffer.FromChars("x"), null), TestContext.Current.CancellationToken));
        var noKey = await Assert.ThrowsAsync<VaultException>(() =>
            vault.UnlockAsync(Fixture.Credentials(keyFile: "manca.keyx"), TestContext.Current.CancellationToken));

        Assert.Equal(VaultError.DatabaseNotFound, noDb.Error);
        Assert.Equal(VaultError.KeyFileNotFound, noKey.Error);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Entry_password_is_read_on_demand_without_trailing_newline()
    {
        var runner = FakeProcessRunner.KeePassXc(ExportXml, password: "gw-secret", showUuid: Gateway.Uuid);
        using var vault = Service(runner);
        await vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken);

        using var password = await vault.ReadPasswordAsync(Gateway, TestContext.Current.CancellationToken);

        Assert.True(password.ContentEquals(Fixture.Utf8("gw-secret")));
        var show = runner.Calls.Single(c => c.Request.Arguments[0] == "show");
        Assert.Equal(
            ["show", "-q", "-k", Fixture.KeyFilePath, "-s", "-a", "Uuid", "-a", "Password", "--", Fixture.DatabasePath, "Clienti/Rossi Srl/Gateway"],
            show.Request.Arguments);
    }

    [Fact]
    public async Task Password_of_another_entry_with_the_same_path_is_refused()
    {
        // S2: keepassxc-cli restituisce la prima voce con quel percorso, per esempio una scaduta.
        var runner = FakeProcessRunner.KeePassXc(ExportXml, password: "password-della-voce-SCADUTA", showUuid: Guid.NewGuid());
        using var vault = Service(runner);
        await vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<VaultException>(() => vault.ReadPasswordAsync(Gateway, TestContext.Current.CancellationToken));

        Assert.Equal(VaultError.EntryMismatch, error.Error);
    }

    [Fact]
    public async Task Empty_password_is_read_as_empty()
    {
        var runner = FakeProcessRunner.KeePassXc(ExportXml, password: string.Empty, showUuid: Gateway.Uuid);
        using var vault = Service(runner);
        await vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken);

        using var password = await vault.ReadPasswordAsync(Gateway, TestContext.Current.CancellationToken);

        Assert.True(password.IsEmpty);
    }

    [Fact]
    public async Task Multi_line_password_keeps_its_inner_newlines()
    {
        var runner = FakeProcessRunner.KeePassXc(ExportXml, password: "riga1\nriga2", showUuid: Gateway.Uuid);
        using var vault = Service(runner);
        await vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken);

        using var password = await vault.ReadPasswordAsync(Gateway, TestContext.Current.CancellationToken);

        Assert.True(password.ContentEquals(Fixture.Utf8("riga1\nriga2")));
    }

    [Fact]
    public async Task Ambiguous_entries_are_never_read()
    {
        var runner = FakeProcessRunner.KeePassXc(ExportXml);
        using var vault = Service(runner);
        await vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<VaultException>(() =>
            vault.ReadPasswordAsync(Gateway with { Warning = EntryWarning.AmbiguousPath }, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(runner.Calls, c => c.Request.Arguments[0] == "show");
    }

    [Fact]
    public async Task After_lock_nothing_can_be_read()
    {
        var runner = FakeProcessRunner.KeePassXc(ExportXml);
        using var vault = Service(runner);
        await vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken);

        vault.Lock();

        Assert.False(vault.IsUnlocked);
        var error = await Assert.ThrowsAsync<VaultException>(() => vault.ReloadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(VaultError.Locked, error.Error);
    }

    [Fact]
    public async Task Only_read_commands_are_used()
    {
        var runner = FakeProcessRunner.KeePassXc(ExportXml, showUuid: Gateway.Uuid);
        using var vault = Service(runner);
        await vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken);
        await vault.ReloadAsync(TestContext.Current.CancellationToken);
        (await vault.ReadPasswordAsync(Gateway, TestContext.Current.CancellationToken)).Dispose();

        Assert.All(runner.Calls, c => Assert.Contains(c.Request.Arguments[0], ReadCommands));
    }
}
