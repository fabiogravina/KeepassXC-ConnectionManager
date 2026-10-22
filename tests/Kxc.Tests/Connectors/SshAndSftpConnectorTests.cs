using Kxc.Connectors.Sftp;
using Kxc.Connectors.Ssh;
using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Platform;
using static Kxc.Tests.Connectors.WindowsRdpConnectorTests;

namespace Kxc.Tests.Connectors;

public class SshAndSftpConnectorTests
{
    private const string Secret = "pi-segreto;$HOME";

    private sealed class RecordingLauncher : IProcessLauncher
    {
        public List<(string File, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string>? Environment)> Started { get; } = [];

        public ILaunchedProcess Start(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
        {
            Started.Add((fileName, arguments, environment));
            return new FakeProcess();
        }
    }

    private sealed class NeverAsked : IHostKeyConfirmation
    {
        public Task<bool> ConfirmAsync(HostKeyQuestion question, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private static ConnectionEntry Entry(string user = "pi", int? port = null, bool agent = false, string host = "192.168.178.210") => new()
    {
        Uuid = Guid.NewGuid(),
        Title = "Casa",
        EntryPath = "Casa",
        GroupPath = ["Interni"],
        Protocols = ConnectionProtocols.Ssh | ConnectionProtocols.Sftp,
        Host = host,
        Port = port,
        UserName = user,
        UsesSshAgent = agent,
    };

    private static WindowsOpenSshConnector OpenSsh(ConnectionProtocols protocol, RecordingLauncher launcher) =>
        new(protocol, new FakeVault(Secret), launcher, new NeverAsked(), "kxc-askpass.exe", clientPath: @"C:\ssh.exe", terminalPath: "wt.exe");

    [Fact]
    public async Task Ssh_opens_in_windows_terminal_with_askpass_and_no_secret_anywhere()
    {
        var launcher = new RecordingLauncher();
        var connector = OpenSsh(ConnectionProtocols.Ssh, launcher);

        await connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken);

        var (file, arguments, environment) = Assert.Single(launcher.Started);
        Assert.Equal("wt.exe", file);
        Assert.Equal(
            ["-w", "new", "nt", "--title", "SSH · Interni · Casa", @"C:\ssh.exe", "-o", "NumberOfPasswordPrompts=1", "-o", "User=pi", "--", "192.168.178.210"],
            arguments);
        Assert.Equal("kxc-askpass.exe", environment!["SSH_ASKPASS"]);
        Assert.Equal("force", environment["SSH_ASKPASS_REQUIRE"]);
        Assert.Equal(connector.LastServer!.ChannelName, environment[AskPassProtocol.ChannelVariable]);
        Assert.DoesNotContain(arguments.Concat(environment.Values), v => v.Contains("segreto", StringComparison.Ordinal));
        connector.CleanUp();
    }

    [Fact]
    public void Sftp_uses_capital_p_for_the_port_and_ssh_lowercase()
    {
        Assert.Equal(["-o", "NumberOfPasswordPrompts=1", "-o", "User=pi", "-P", "2222", "--", "h"],
            WindowsOpenSshConnector.ClientArguments(ConnectionProtocols.Sftp, Entry(port: 2222), "h"));
        Assert.Equal(["-o", "NumberOfPasswordPrompts=1", "-o", "User=pi", "-p", "2222", "--", "h"],
            WindowsOpenSshConnector.ClientArguments(ConnectionProtocols.Ssh, Entry(port: 2222), "h"));
    }

    [Fact]
    public void Semicolons_are_escaped_and_the_title_is_cleaned_for_windows_terminal()
    {
        var arguments = WindowsOpenSshConnector.TerminalArguments("A;\"B\\`\u0007C", "ssh.exe", ["-o", "Opzione=a;b"]);

        Assert.Equal(["-w", "new", "nt", "--title", "ABC", "ssh.exe", "-o", "Opzione=a\\;b"], arguments);
    }

    [Fact]
    public void Arguments_with_quotes_never_reach_windows_terminal()
    {
        // S1: wt.exe ricompone la riga di comando e spezza gli argomenti con virgolette (prova del 22/10/2026).
        var error = Assert.Throws<ConnectionException>(() =>
            WindowsOpenSshConnector.TerminalArguments("T", "ssh.exe", ["-o", "User=x\" -oProxyCommand=calc \""]));

        Assert.Equal(ConnectionError.InvalidEntry, error.Error);
    }

    [Theory]
    [InlineData("x\" -oProxyCommand=calc \"")]
    [InlineData("a b")]
    [InlineData("$(calc)")]
    [InlineData("-oProxyCommand=calc")]
    public async Task Unsafe_ssh_user_names_are_refused_before_reading_the_password(string user)
    {
        var launcher = new RecordingLauncher();
        var connector = OpenSsh(ConnectionProtocols.Ssh, launcher);

        var error = await Assert.ThrowsAsync<ConnectionException>(() => connector.LaunchAsync(Entry(user: user), TestContext.Current.CancellationToken));

        Assert.Equal(ConnectionError.InvalidEntry, error.Error);
        Assert.Empty(launcher.Started);
        Assert.Null(connector.LastServer);
    }

    [Fact]
    public async Task With_the_ssh_agent_no_password_is_read()
    {
        var launcher = new RecordingLauncher();
        var connector = OpenSsh(ConnectionProtocols.Ssh, launcher);

        await connector.LaunchAsync(Entry(agent: true), TestContext.Current.CancellationToken);

        Assert.Single(launcher.Started);
        connector.CleanUp();
    }

    [Fact]
    public void WinScp_gets_the_session_url_and_the_pipe_path_never_the_password()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "WinSCP: solo Windows");
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var arguments = WinScpConnector.Arguments(Entry(user: "ad@min", port: 2222), "srv.example", @"\\.\pipe\kxc-secret-1");

        Assert.Equal(["sftp://ad%40min@srv.example:2222/", "/passwordsfromfiles", @"/password=\\.\pipe\kxc-secret-1"], arguments);
        Assert.Equal(["sftp://pi@[fe80::1]/"], WinScpConnector.Arguments(Entry(), "fe80::1", null));
    }

    [Fact]
    public async Task WinScp_reads_the_password_once_from_the_pipe()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Named pipe come file: solo Windows");
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var launcher = new RecordingLauncher();
        var connector = new WinScpConnector(new FakeVault(Secret), launcher, "WinSCP.exe");

        await connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken);

        var (_, arguments, _) = Assert.Single(launcher.Started);
        var pipePath = arguments[^1]["/password=".Length..];
        Assert.Equal(Secret, await File.ReadAllTextAsync(pipePath, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<IOException>(() => File.ReadAllTextAsync(pipePath, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(arguments, a => a.Contains("segreto", StringComparison.Ordinal));
        connector.CleanUp();
    }
}
