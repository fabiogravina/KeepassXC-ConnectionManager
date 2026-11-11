using Kxc.Connectors.Ssh;
using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Platform;
using static Kxc.Tests.Connectors.WindowsRdpConnectorTests;

namespace Kxc.Tests.Connectors;

public sealed class LinuxOpenSshConnectorTests : IDisposable
{
    private const string Secret = "pi-segreto;$HOME";
    private readonly string _askPass = Path.Combine(Path.GetTempPath(), "kxc-askpass-test-" + Guid.NewGuid().ToString("N"));

    public LinuxOpenSshConnectorTests() => File.WriteAllText(_askPass, string.Empty);

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

    private static LinuxTerminal Terminal(string id) => LinuxTerminal.Known.Single(t => t.Id == id);

    private static ConnectionEntry Entry(string user = "pi", int? port = null) => new()
    {
        Uuid = Guid.NewGuid(),
        Title = "Casa",
        EntryPath = "Casa",
        GroupPath = ["Interni"],
        Protocols = ConnectionProtocols.Ssh | ConnectionProtocols.Sftp,
        Host = "192.168.178.210",
        Port = port,
        UserName = user,
    };

    private LinuxOpenSshConnector Connector(ConnectionProtocols protocol, RecordingLauncher launcher, string terminal = "konsole", string? askPass = null) =>
        new(protocol, new FakeVault(Secret), launcher, new NeverAsked(), askPass ?? _askPass, Terminal(terminal), "/usr/bin/" + terminal,
            clientPath: protocol == ConnectionProtocols.Ssh ? "/usr/bin/ssh" : "/usr/bin/sftp", envPath: "/usr/bin/env");

    [Fact]
    public async Task Ssh_opens_in_konsole_with_askpass_through_env_and_no_secret_anywhere()
    {
        var launcher = new RecordingLauncher();
        var connector = Connector(ConnectionProtocols.Ssh, launcher);

        await connector.LaunchAsync(Entry(), TestContext.Current.CancellationToken);

        var (file, arguments, environment) = Assert.Single(launcher.Started);
        Assert.Equal("/usr/bin/konsole", file);
        Assert.Null(environment);
        Assert.Equal(
            [
                "-p", "tabtitle=SSH · Interni · Casa", "-e", "/usr/bin/env",
                AskPassProtocol.ChannelVariable + "=" + connector.LastServer!.ChannelName,
                "SSH_ASKPASS=" + _askPass, "SSH_ASKPASS_REQUIRE=force",
                "/usr/bin/ssh", "-o", "NumberOfPasswordPrompts=1", "-o", "ForwardAgent=no", "-o", "User=pi", "--", "192.168.178.210",
            ],
            arguments);
        Assert.DoesNotContain(arguments, a => a.Contains("segreto", StringComparison.Ordinal));
        connector.CleanUp();
    }

    [Fact]
    public async Task Sftp_uses_the_sftp_client_and_capital_p_for_the_port()
    {
        var launcher = new RecordingLauncher();
        var connector = Connector(ConnectionProtocols.Sftp, launcher, terminal: "gnome-terminal");

        await connector.LaunchAsync(Entry(port: 2222), TestContext.Current.CancellationToken);

        var (file, arguments, _) = Assert.Single(launcher.Started);
        Assert.Equal("/usr/bin/gnome-terminal", file);
        Assert.Equal(["--title", "SFTP · Interni · Casa", "--", "/usr/bin/env"], arguments.Take(4));
        Assert.Equal(["/usr/bin/sftp", "-o", "NumberOfPasswordPrompts=1", "-o", "ForwardAgent=no", "-o", "User=pi", "-P", "2222", "--", "192.168.178.210"], arguments.Skip(7));
        connector.CleanUp();
    }

    [Fact]
    public void Every_terminal_gets_the_command_unchanged_at_the_end()
    {
        IReadOnlyList<string> command = ["/usr/bin/env", "A=b", "/usr/bin/ssh", "-o", "User=pi", "--", "h"];

        foreach (var terminal in LinuxTerminal.Known)
        {
            var arguments = terminal.Arguments("Titolo", command);
            Assert.Equal(command, arguments.TakeLast(command.Count));
        }
    }

    [Fact]
    public void Konsole_title_cannot_add_profile_properties()
    {
        // Konsole divide il valore di -p con ';': un titolo con ';' potrebbe impostare Command=.
        var title = OpenSshConnectorBase.SafeTitle("Casa;Command=/bin/sh");

        Assert.Equal(["-p", "tabtitle=CasaCommand=/bin/sh", "-e", "/usr/bin/env"], Terminal("konsole").Arguments(title, ["/usr/bin/env"]));
    }

    [Theory]
    [InlineData("x\" -oProxyCommand=calc \"")]
    [InlineData("$(calc)")]
    [InlineData("-oProxyCommand=calc")]
    public async Task Unsafe_ssh_user_names_are_refused_before_reading_the_password(string user)
    {
        var launcher = new RecordingLauncher();
        var connector = Connector(ConnectionProtocols.Ssh, launcher);

        var error = await Assert.ThrowsAsync<ConnectionException>(() => connector.LaunchAsync(Entry(user: user), TestContext.Current.CancellationToken));

        Assert.Equal(ConnectionError.InvalidEntry, error.Error);
        Assert.Empty(launcher.Started);
        Assert.Null(connector.LastServer);
    }

    [Fact]
    public void Not_supported_without_the_askpass_helper()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Connettore Linux");

        var connector = Connector(ConnectionProtocols.Ssh, new RecordingLauncher(), askPass: "/nessuna/cartella/kxc-askpass");

        Assert.False(connector.IsSupported);
    }

    [Theory]
    [InlineData("KDE", "konsole,ptyxis,kgx,gnome-terminal,xfce4-terminal,kitty,alacritty,foot,xterm")]
    [InlineData("ubuntu:GNOME", "ptyxis,kgx,gnome-terminal,konsole,xfce4-terminal,kitty,alacritty,foot,xterm")]
    [InlineData("XFCE", "xfce4-terminal,konsole,ptyxis,kgx,gnome-terminal,kitty,alacritty,foot,xterm")]
    [InlineData("", "konsole,ptyxis,kgx,gnome-terminal,xfce4-terminal,kitty,alacritty,foot,xterm")]
    public void Terminals_of_the_current_desktop_come_first(string desktop, string expected)
    {
        Assert.Equal(expected, string.Join(',', LinuxTerminal.Ordered(desktop).Select(t => t.Id)));
    }

    public void Dispose() => File.Delete(_askPass);
}
