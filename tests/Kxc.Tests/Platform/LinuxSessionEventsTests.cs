using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Kxc.Core.Platform;
using Kxc.Platform.Session;
using Tmds.DBus.Protocol;

namespace Kxc.Tests.Platform;

/// <summary>
/// S15: blocco su Linux dai segnali D-Bus. Un dbus-daemon privato fa da bus di sistema e di
/// sessione; un secondo client impersona logind e i salvaschermi di KDE e GNOME. Saltati fuori da
/// Linux o se dbus-daemon non è installato.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSessionEventsTests
{
    private const string OwnSession = "/org/freedesktop/login1/session/_32";
    private const string OtherSession = "/org/freedesktop/login1/session/_33";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly string[] DaemonPaths = ["/usr/bin/dbus-daemon", "/bin/dbus-daemon"];

    [Fact]
    public async Task S15_sleep_screen_saver_and_session_lock_lock_the_app()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "D-Bus: solo Linux");
        var daemon = DaemonPaths.FirstOrDefault(File.Exists);
        Assert.SkipWhen(daemon is null, "dbus-daemon non installato");

        await using var bus = await PrivateBus.StartAsync(daemon!);
        using var services = new DBusConnection(bus.Address);
        await services.ConnectAsync();
        var logind = new FakeLogind();
        services.AddMethodHandler(logind);
        string[] names = [LinuxSessionEvents.Login1, .. LinuxSessionEvents.ScreenSavers];
        foreach (var name in names)
        {
            await services.RequestNameAsync(name, RequestNameOptions.Default);
        }

        using var events = new LinuxSessionEvents(bus.Address, bus.Address);
        var reasons = Channel.CreateUnbounded<SystemLockReason>();
        events.LockRequired += (_, reason) => reasons.Writer.TryWrite(reason);
        await events.Started.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal("auto", logind.RequestedSession);

        // Il risveglio (false) non blocca, l'inizio della sospensione sì.
        Emit(services, LinuxSessionEvents.Login1Path, LinuxSessionEvents.Login1Manager, "PrepareForSleep", false);
        Emit(services, LinuxSessionEvents.Login1Path, LinuxSessionEvents.Login1Manager, "PrepareForSleep", true);
        Assert.Equal(SystemLockReason.Suspending, await NextAsync(reasons));

        // Salvaschermo di KDE (il percorso non conta) e di GNOME.
        Emit(services, "/ScreenSaver", "org.freedesktop.ScreenSaver", "ActiveChanged", false);
        Emit(services, "/ScreenSaver", "org.freedesktop.ScreenSaver", "ActiveChanged", true);
        Assert.Equal(SystemLockReason.SessionLocked, await NextAsync(reasons));
        Emit(services, "/org/gnome/ScreenSaver", "org.gnome.ScreenSaver", "ActiveChanged", true);
        Assert.Equal(SystemLockReason.SessionLocked, await NextAsync(reasons));

        // Sessione di logind dell'app: segnale Lock e LockedHint.
        Emit(services, OwnSession, LinuxSessionEvents.Login1Session, "Lock");
        Assert.Equal(SystemLockReason.SessionLocked, await NextAsync(reasons));
        EmitLockedHint(services, OwnSession, false);
        EmitLockedHint(services, OwnSession, true);
        Assert.Equal(SystemLockReason.SessionLocked, await NextAsync(reasons));

        // Le altre sessioni non contano. I segnali dello stesso mittente arrivano in ordine, quindi
        // se il prossimo evento è la sospensione i due precedenti sono stati ignorati.
        Emit(services, OtherSession, LinuxSessionEvents.Login1Session, "Lock");
        EmitLockedHint(services, OtherSession, true);
        Emit(services, LinuxSessionEvents.Login1Path, LinuxSessionEvents.Login1Manager, "PrepareForSleep", true);
        Assert.Equal(SystemLockReason.Suspending, await NextAsync(reasons));

        // Un client che non possiede il nome del salvaschermo non viene ascoltato. La chiamata al bus
        // dopo l'invio garantisce che il suo segnale sia già stato instradato prima del successivo.
        using (var impostor = new DBusConnection(bus.Address))
        {
            await impostor.ConnectAsync();
            Emit(impostor, "/ScreenSaver", "org.freedesktop.ScreenSaver", "ActiveChanged", true);
            await impostor.ListServicesAsync();
        }

        Emit(services, LinuxSessionEvents.Login1Path, LinuxSessionEvents.Login1Manager, "PrepareForSleep", true);
        Assert.Equal(SystemLockReason.Suspending, await NextAsync(reasons));
        Assert.False(reasons.Reader.TryRead(out _));
    }

    [Fact]
    public async Task S15_missing_services_do_not_break_the_other_sources()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "D-Bus: solo Linux");
        var daemon = DaemonPaths.FirstOrDefault(File.Exists);
        Assert.SkipWhen(daemon is null, "dbus-daemon non installato");

        // Niente logind sul bus: GetSession fallisce, il salvaschermo funziona lo stesso.
        await using var bus = await PrivateBus.StartAsync(daemon!);
        using var services = new DBusConnection(bus.Address);
        await services.ConnectAsync();
        await services.RequestNameAsync("org.freedesktop.ScreenSaver", RequestNameOptions.Default);

        using var events = new LinuxSessionEvents(bus.Address, bus.Address);
        var reasons = Channel.CreateUnbounded<SystemLockReason>();
        events.LockRequired += (_, reason) => reasons.Writer.TryWrite(reason);
        await events.Started.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Emit(services, "/org/freedesktop/ScreenSaver", "org.freedesktop.ScreenSaver", "ActiveChanged", true);
        Assert.Equal(SystemLockReason.SessionLocked, await NextAsync(reasons));
    }

    [Fact]
    public async Task S15_bus_not_reachable_is_not_an_error()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "D-Bus: solo Linux");

        var missing = "unix:path=" + Path.Combine(Path.GetTempPath(), "kxc-nessun-bus-" + Guid.NewGuid().ToString("N"));
        using var events = new LinuxSessionEvents(missing, missing);

        await events.Started.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    private static async Task<SystemLockReason> NextAsync(Channel<SystemLockReason> reasons) =>
        await reasons.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Timeout);

    private static void Emit(DBusConnection connection, string path, string @interface, string member, bool? value = null)
    {
        var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(null, path, @interface, member, value is null ? null : "b");
        if (value is bool flag)
        {
            writer.WriteBool(flag);
        }

        Assert.True(connection.TrySendMessage(writer.CreateMessage()));
    }

    // Come logind: PropertiesChanged con più proprietà, LockedHint non per prima.
    private static void EmitLockedHint(DBusConnection connection, string path, bool locked)
    {
        var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(null, path, LinuxSessionEvents.Properties, "PropertiesChanged", "sa{sv}as");
        writer.WriteString(LinuxSessionEvents.Login1Session);
        var properties = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("IdleHint");
        writer.WriteVariant(VariantValue.Bool(false));
        writer.WriteDictionaryEntryStart();
        writer.WriteString("LockedHint");
        writer.WriteVariant(VariantValue.Bool(locked));
        writer.WriteDictionaryEnd(properties);
        writer.WriteArray(Array.Empty<string>());
        Assert.True(connection.TrySendMessage(writer.CreateMessage()));
    }

    private sealed class FakeLogind : IPathMethodHandler
    {
        public string? RequestedSession { get; private set; }

        public string Path => LinuxSessionEvents.Login1Path;

        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var request = context.Request;
            if (request.InterfaceAsString == LinuxSessionEvents.Login1Manager && request.MemberAsString == "GetSession")
            {
                RequestedSession = request.GetBodyReader().ReadString();
                var writer = context.CreateReplyWriter("o");
                writer.WriteObjectPath(OwnSession);
                context.Reply(writer.CreateMessage());
            }
            else
            {
                context.ReplyUnknownMethodError();
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>dbus-daemon con una configurazione minima, chiuso alla fine del test.</summary>
    private sealed class PrivateBus : IAsyncDisposable
    {
        private const string Configuration = """
            <!DOCTYPE busconfig PUBLIC "-//freedesktop//DTD D-Bus Bus Configuration 1.0//EN"
              "http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd">
            <busconfig>
              <type>session</type>
              <listen>unix:tmpdir=/tmp</listen>
              <auth>EXTERNAL</auth>
              <policy context="default">
                <allow send_destination="*" eavesdrop="true"/>
                <allow eavesdrop="true"/>
                <allow own="*"/>
              </policy>
            </busconfig>
            """;

        private readonly Process _process;
        private readonly string _configuration;

        private PrivateBus(Process process, string configuration, string address)
        {
            _process = process;
            _configuration = configuration;
            Address = address;
        }

        public string Address { get; }

        public static async Task<PrivateBus> StartAsync(string daemon)
        {
            var configuration = Path.Combine(Path.GetTempPath(), "kxc-dbus-" + Guid.NewGuid().ToString("N") + ".conf");
            await File.WriteAllTextAsync(configuration, Configuration);

            var info = new ProcessStartInfo(daemon) { RedirectStandardOutput = true, UseShellExecute = false };
            info.ArgumentList.Add("--config-file=" + configuration);
            info.ArgumentList.Add("--nofork");
            info.ArgumentList.Add("--print-address");
            var process = Process.Start(info) ?? throw new InvalidOperationException("dbus-daemon non avviato.");
            var address = await process.StandardOutput.ReadLineAsync().WaitAsync(Timeout);
            Assert.False(string.IsNullOrWhiteSpace(address), "dbus-daemon non ha indicato l'indirizzo.");
            return new PrivateBus(process, configuration, address.Trim());
        }

        public async ValueTask DisposeAsync()
        {
            _process.Kill();
            await _process.WaitForExitAsync();
            _process.Dispose();
            File.Delete(_configuration);
        }
    }
}
