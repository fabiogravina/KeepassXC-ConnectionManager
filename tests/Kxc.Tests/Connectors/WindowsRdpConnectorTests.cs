using System.Text;
using Kxc.Connectors;
using Kxc.Connectors.Rdp;
using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Platform;
using Kxc.Core.Security;
using Kxc.Core.Vault;
using Microsoft.Extensions.Time.Testing;

namespace Kxc.Tests.Connectors;

public class WindowsRdpConnectorTests
{
    private const string Secret = "Pa55-è€!";

    internal sealed class FakeVault(string password, VaultError? error = null) : IVaultService
    {
        public bool IsUnlocked => true;

        public Task<VaultSnapshot> UnlockAsync(UnlockCredentials credentials, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<VaultSnapshot> ReloadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SecretBuffer> ReadPasswordAsync(ConnectionEntry entry, CancellationToken cancellationToken) =>
            error is { } e ? throw new VaultException(e, "errore simulato") : Task.FromResult(SecretBuffer.FromChars(password));

        public void Lock()
        {
        }
    }

    internal sealed class FakeCredentials : ISystemCredentialStore
    {
        public Dictionary<string, (string User, string Password)> Stored { get; } = [];

        public List<string> Deleted { get; } = [];

        public void WriteSessionCredential(string target, string userName, ReadOnlySpan<byte> utf8Password) =>
            Stored[target] = (userName, Encoding.UTF8.GetString(utf8Password));

        public bool Delete(string target)
        {
            Deleted.Add(target);
            return Stored.Remove(target);
        }
    }

    internal sealed class FakeProcess : ILaunchedProcess
    {
        public int Id => 42;

        public bool HasExited { get; set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    internal sealed class FakeLauncher : IProcessLauncher
    {
        public List<(string File, IReadOnlyList<string> Arguments)> Started { get; } = [];

        public FakeProcess Process { get; } = new();

        public bool Fail { get; set; }

        public ILaunchedProcess Start(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
        {
            if (Fail)
            {
                throw new InvalidOperationException("client mancante");
            }

            Started.Add((fileName, arguments));
            return Process;
        }
    }

    internal sealed class FakeProbe : ITcpConnectionProbe
    {
        public HashSet<string> Endpoints { get; } = new(StringComparer.Ordinal);

        public IReadOnlySet<string> EstablishedEndpoints(int remotePort) =>
            Endpoints.Where(e => e.EndsWith(":" + remotePort, StringComparison.Ordinal)).ToHashSet();
    }

    private sealed record Harness(WindowsRdpConnector Connector, FakeCredentials Credentials, FakeLauncher Launcher, FakeProbe Probe, FakeTimeProvider Time);

    private static Harness Create(VaultError? vaultError = null)
    {
        var credentials = new FakeCredentials();
        var launcher = new FakeLauncher();
        var probe = new FakeProbe();
        var time = new FakeTimeProvider();
        var connector = new WindowsRdpConnector(new FakeVault(Secret, vaultError), credentials, launcher, probe, time, mstscPath: "mstsc.exe");
        return new Harness(connector, credentials, launcher, probe, time);
    }

    /// <summary>Avanza l'orologio simulato a passi brevi, lasciando girare le continuazioni.</summary>
    private static async Task RunClockAsync(Harness h, TimeSpan total)
    {
        var step = TimeSpan.FromMilliseconds(250);
        for (var elapsed = TimeSpan.Zero; elapsed < total; elapsed += step)
        {
            h.Time.Advance(step);
            await Task.Delay(2, TestContext.Current.CancellationToken);
        }

        if (h.Connector.PendingCleanup is { IsCompleted: false } pending)
        {
            await Task.WhenAny(pending, Task.Delay(200, TestContext.Current.CancellationToken));
        }
    }

    private static ConnectionEntry Entry(string host = "192.168.1.125", int? port = null, string user = @"OLTRESOLUTIONS\fabio", string? domain = null) => new()
    {
        Uuid = Guid.NewGuid(),
        Title = "PcFabio",
        EntryPath = "PcFabio",
        Protocols = ConnectionProtocols.Rdp,
        Host = host,
        Port = port,
        UserName = user,
        Domain = domain,
    };

    [Fact]
    public async Task Password_goes_to_the_credential_store_never_to_mstsc_arguments()
    {
        var h = Create();

        await h.Connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken);

        Assert.Equal((@"OLTRESOLUTIONS\fabio", Secret), h.Credentials.Stored["TERMSRV/192.168.1.125"]);
        var (file, arguments) = Assert.Single(h.Launcher.Started);
        Assert.Equal("mstsc.exe", file);
        Assert.Equal(["/v:192.168.1.125"], arguments);
        Assert.DoesNotContain(arguments, a => a.Contains(Secret, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("srv.example", 3389, "/v:srv.example")]
    [InlineData("srv.example", 3390, "/v:srv.example:3390")]
    [InlineData("fe80::1", 3390, "/v:[fe80::1]:3390")]
    public void Port_is_added_only_when_not_default(string host, int port, string expected)
    {
        Assert.Equal([expected], WindowsRdpConnector.Arguments(host, port));
    }

    [Theory]
    [InlineData("admin", "ROSSI", @"ROSSI\admin")]
    [InlineData(@"ROSSI\admin", "ALTRO", @"ROSSI\admin")]
    [InlineData("admin@rossi.example", "ROSSI", "admin@rossi.example")]
    [InlineData("admin", null, "admin")]
    public void Domain_field_is_joined_to_the_user(string user, string? domain, string expected)
    {
        Assert.Equal(expected, WindowsRdpConnector.UserName(Entry(user: user, domain: domain)));
    }

    [Fact]
    public async Task Credential_is_removed_after_the_connection_and_the_grace_period()
    {
        var h = Create();
        await h.Connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken);

        h.Probe.Endpoints.Add("192.168.1.125:3389");
        await RunClockAsync(h, TimeSpan.FromSeconds(1));
        Assert.Contains("TERMSRV/192.168.1.125", h.Credentials.Stored.Keys);

        await RunClockAsync(h, WindowsRdpConnector.CredentialGrace + TimeSpan.FromSeconds(1));

        Assert.True(h.Connector.PendingCleanup!.IsCompleted);
        Assert.Empty(h.Credentials.Stored);
    }

    [Fact]
    public async Task Connections_already_open_before_the_launch_do_not_count()
    {
        var h = Create();
        h.Probe.Endpoints.Add("192.168.1.125:3389");
        await h.Connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken);

        await RunClockAsync(h, TimeSpan.FromSeconds(20));

        Assert.NotEmpty(h.Credentials.Stored);
    }

    [Fact]
    public async Task Credential_is_removed_when_mstsc_exits_or_after_the_timeout()
    {
        var exited = Create();
        await exited.Connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken);
        exited.Launcher.Process.HasExited = true;
        await RunClockAsync(exited, TimeSpan.FromSeconds(1));
        Assert.Empty(exited.Credentials.Stored);

        var timeout = Create();
        await timeout.Connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken);
        await RunClockAsync(timeout, WindowsRdpConnector.ConnectTimeout - TimeSpan.FromSeconds(5));
        Assert.NotEmpty(timeout.Credentials.Stored);
        await RunClockAsync(timeout, TimeSpan.FromSeconds(10));
        Assert.Empty(timeout.Credentials.Stored);
    }

    [Fact]
    public async Task Clean_up_removes_pending_credentials_at_lock_and_exit()
    {
        var h = Create();
        await h.Connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken);

        h.Connector.CleanUp();

        Assert.Empty(h.Credentials.Stored);
        Assert.Equal(["TERMSRV/192.168.1.125"], h.Credentials.Deleted);
    }

    [Fact]
    public async Task Without_user_mstsc_starts_and_asks_for_credentials_itself()
    {
        var h = Create();

        await h.Connector.LaunchAsync(Entry(user: ""), TestContext.Current.CancellationToken);

        Assert.Empty(h.Credentials.Stored);
        Assert.Single(h.Launcher.Started);
    }

    [Fact]
    public async Task Vault_errors_and_missing_client_are_reported_and_leave_no_credential()
    {
        var vault = Create(VaultError.EntryNotFound);
        var vaultError = await Assert.ThrowsAsync<ConnectionException>(() => vault.Connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken));
        Assert.Equal(ConnectionError.Vault, vaultError.Error);
        Assert.Equal(VaultError.EntryNotFound, vaultError.VaultError);
        Assert.Empty(vault.Launcher.Started);

        var client = Create();
        client.Launcher.Fail = true;
        var clientError = await Assert.ThrowsAsync<ConnectionException>(() => client.Connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken));
        Assert.Equal(ConnectionError.ClientNotFound, clientError.Error);
        Assert.Empty(client.Credentials.Stored);
    }

    [Fact]
    public async Task Launcher_refuses_entries_with_warnings_and_unknown_protocols()
    {
        var h = Create();
        using var launcher = new ConnectionLauncher([h.Connector]);

        var invalid = await Assert.ThrowsAsync<ConnectionException>(() =>
            launcher.LaunchAsync(Entry() with { Warning = EntryWarning.MissingHost }, ConnectionProtocols.Rdp, TestContext.Current.CancellationToken));
        var ssh = await Assert.ThrowsAsync<ConnectionException>(() =>
            launcher.LaunchAsync(Entry() with { Protocols = ConnectionProtocols.Ssh }, ConnectionProtocols.Ssh, TestContext.Current.CancellationToken));

        Assert.Equal(ConnectionError.InvalidEntry, invalid.Error);
        Assert.Equal(ConnectionError.NotSupported, ssh.Error);
        Assert.Empty(h.Launcher.Started);
    }
}
