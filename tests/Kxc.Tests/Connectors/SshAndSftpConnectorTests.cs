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

    /// <summary>Conferma della host key che non risponde mai, nemmeno all'annullamento (finestra aperta).</summary>
    private sealed class PendingForever : IHostKeyConfirmation
    {
        public TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> ConfirmAsync(HostKeyQuestion question, CancellationToken cancellationToken)
        {
            Asked.TrySetResult();
            return new TaskCompletionSource<bool>().Task;
        }
    }

    private static WindowsOpenSshConnector OpenSsh(ConnectionProtocols protocol, RecordingLauncher launcher, FakeVault? vault = null, IHostKeyConfirmation? hostKeys = null) =>
        new(protocol, vault ?? new FakeVault(Secret), launcher, hostKeys ?? new NeverAsked(), "kxc-askpass.exe", clientPath: @"C:\ssh.exe", terminalPath: "wt.exe");

    [Fact]
    public async Task S4_clean_up_returns_at_once_even_with_a_host_key_question_pending()
    {
        var hostKeys = new PendingForever();
        var vault = new FakeVault(Secret);
        var connector = OpenSsh(ConnectionProtocols.Ssh, new RecordingLauncher(), vault, hostKeys);
        await connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken);

        // Un vero kxc-askpass fa la domanda sulla host key e resta in attesa della risposta.
        var helper = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "kxc-askpass.exe" : "kxc-askpass"))
        {
            ArgumentList = { "The authenticity of host 'h' can't be established. Are you sure (yes/no/[fingerprint])? " },
            Environment = { [AskPassProtocol.ChannelVariable] = connector.LastServer!.ChannelName },
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        try
        {
            await hostKeys.Asked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            // Prima della correzione CleanUp restava in attesa della risposta (deadlock sul thread dell'interfaccia).
            await Task.Run(connector.CleanUp, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.Throws<ObjectDisposedException>(() => vault.Issued[0].Span.Length);
        }
        finally
        {
            helper.Kill();
            helper.Dispose();
        }
    }

    [Fact]
    public async Task S10_lock_during_the_password_read_starts_nothing()
    {
        var vault = new FakeVault(Secret);
        var launcher = new RecordingLauncher();
        var connector = OpenSsh(ConnectionProtocols.Ssh, launcher, vault);
        using var session = new CancellationTokenSource();
        vault.DuringRead = session.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connector.LaunchAsync(Entry(), session.Token));

        Assert.Empty(launcher.Started);
        Assert.Null(connector.LastServer);
        Assert.Throws<ObjectDisposedException>(() => vault.Issued[0].Span.Length);
    }

    [Fact]
    public async Task Ssh_opens_in_windows_terminal_with_askpass_and_no_secret_anywhere()
    {
        var launcher = new RecordingLauncher();
        var connector = OpenSsh(ConnectionProtocols.Ssh, launcher);

        await connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken);

        var (file, arguments, environment) = Assert.Single(launcher.Started);
        Assert.Equal("wt.exe", file);
        Assert.Equal(
            ["-w", "new", "nt", "--title", "SSH · Interni · Casa", @"C:\ssh.exe", "-o", "NumberOfPasswordPrompts=1", "-o", "ForwardAgent=no", "-o", "User=pi", "--", "192.168.178.210"],
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
        Assert.Equal(["-o", "NumberOfPasswordPrompts=1", "-o", "ForwardAgent=no", "-o", "User=pi", "-P", "2222", "--", "h"],
            WindowsOpenSshConnector.ClientArguments(ConnectionProtocols.Sftp, Entry(port: 2222), "h"));
        Assert.Equal(["-o", "NumberOfPasswordPrompts=1", "-o", "ForwardAgent=no", "-o", "User=pi", "-p", "2222", "--", "h"],
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
