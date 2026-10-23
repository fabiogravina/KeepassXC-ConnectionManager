using System.Runtime.Versioning;
using Kxc.Core.Platform;
using Tmds.DBus.Protocol;

namespace Kxc.Platform.Session;

/// <summary>
/// Blocco dello schermo e sospensione su Linux (S15), dai segnali D-Bus. Gli ambienti desktop non
/// usano tutti la stessa fonte, quindi se ne ascoltano diverse: logind per la sospensione
/// (PrepareForSleep), il blocco della sessione (Lock) e LockedHint; il salvaschermo di KDE
/// (org.freedesktop.ScreenSaver) e di GNOME (org.gnome.ScreenSaver). Lo stesso blocco può quindi
/// arrivare più volte: non è un problema, perché bloccare un'app già bloccata non fa nulla.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSessionEvents : ISystemSessionEvents
{
    internal const string Login1 = "org.freedesktop.login1";
    internal const string Login1Path = "/org/freedesktop/login1";
    internal const string Login1Manager = "org.freedesktop.login1.Manager";
    internal const string Login1Session = "org.freedesktop.login1.Session";
    internal const string Properties = "org.freedesktop.DBus.Properties";
    internal static readonly string[] ScreenSavers = ["org.freedesktop.ScreenSaver", "org.gnome.ScreenSaver"];

    private readonly Lazy<DBusConnection> _system;
    private readonly Lazy<DBusConnection> _session;
    private readonly bool _ownsConnections;
    private readonly List<IDisposable> _watches = [];
    private readonly Lock _gate = new();
    private volatile bool _disposed;

    /// <summary>Bus di sistema e di sessione condivisi (gli stessi che usa Avalonia).</summary>
    public LinuxSessionEvents()
        : this(new Lazy<DBusConnection>(() => DBusConnection.System), new Lazy<DBusConnection>(() => DBusConnection.Session), ownsConnections: false)
    {
    }

    /// <summary>Per i test: bus privati indicati per indirizzo.</summary>
    internal LinuxSessionEvents(string systemAddress, string sessionAddress)
        : this(new Lazy<DBusConnection>(() => Connect(systemAddress)), new Lazy<DBusConnection>(() => Connect(sessionAddress)), ownsConnections: true)
    {
    }

    private LinuxSessionEvents(Lazy<DBusConnection> system, Lazy<DBusConnection> session, bool ownsConnections)
    {
        _system = system;
        _session = session;
        _ownsConnections = ownsConnections;
        Started = Task.Run(StartAsync);
    }

    public event EventHandler<SystemLockReason>? LockRequired;

    public bool IsSupported => true;

    /// <summary>Completato quando ogni sottoscrizione è attiva o risulta non disponibile.</summary>
    internal Task Started { get; }

    public void Dispose()
    {
        IDisposable[] watches;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            watches = [.. _watches];
            _watches.Clear();
        }

        foreach (var watch in watches)
        {
            watch.Dispose();
        }

        if (_ownsConnections)
        {
            if (_system.IsValueCreated)
            {
                _system.Value.Dispose();
            }

            if (_session.IsValueCreated)
            {
                _session.Value.Dispose();
            }
        }
    }

    /// <summary>Valore di LockedHint in un PropertiesChanged di una sessione di logind (firma sa{sv}as).</summary>
    internal static bool ReadLockedHint(Message message)
    {
        var reader = message.GetBodyReader();
        if (reader.ReadString() != Login1Session)
        {
            return false;
        }

        var end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            var name = reader.ReadString();
            var value = reader.ReadVariantValue();
            if (name == "LockedHint")
            {
                return value.GetBool();
            }
        }

        return false;
    }

    private static DBusConnection Connect(string address) =>
        new(new DBusConnectionOptions(address) { AutoConnect = true });

    // Ogni fonte è indipendente: un bus o un servizio che manca (niente logind, nessun salvaschermo)
    // non impedisce di ascoltare le altre.
    private async Task StartAsync()
    {
        await SubscribeAsync(_system, WatchSleepAsync).ConfigureAwait(false);
        await SubscribeAsync(_system, WatchLogindSessionAsync).ConfigureAwait(false);
        foreach (var name in ScreenSavers)
        {
            await SubscribeAsync(_session, bus => WatchScreenSaverAsync(bus, name)).ConfigureAwait(false);
        }
    }

    private static async Task SubscribeAsync(Lazy<DBusConnection> bus, Func<DBusConnection, Task> subscribe)
    {
        try
        {
            await subscribe(bus.Value).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DBusExceptionBase or InvalidOperationException or NotSupportedException
            or IOException or ArgumentException or System.Net.Sockets.SocketException)
        {
            // Fonte non disponibile su questo sistema.
        }
    }

    private async Task WatchSleepAsync(DBusConnection bus) =>
        Track(await bus.WatchSignalAsync(Login1, Login1Path, Login1Manager, "PrepareForSleep",
            static (message, _) => message.GetBodyReader().ReadBool(),
            notification => RaiseIf(IsTrue(notification), SystemLockReason.Suspending),
            ObserverFlags.None, false, null).ConfigureAwait(false));

    // Solo la sessione dell'app: i segnali di logind sul bus di sistema riguardano tutte le sessioni.
    private async Task WatchLogindSessionAsync(DBusConnection bus)
    {
        var path = await FindOwnSessionAsync(bus).ConfigureAwait(false);

        Track(await bus.WatchSignalAsync(Login1, path, Login1Session, "Lock",
            notification => RaiseIf(notification.Type == NotificationType.Value, SystemLockReason.SessionLocked),
            ObserverFlags.None, false, null).ConfigureAwait(false));

        Track(await bus.WatchSignalAsync(Login1, path, Properties, "PropertiesChanged",
            static (message, _) => ReadLockedHint(message),
            notification => RaiseIf(IsTrue(notification), SystemLockReason.SessionLocked),
            ObserverFlags.None, false, null).ConfigureAwait(false));
    }

    private async Task WatchScreenSaverAsync(DBusConnection bus, string name)
    {
        // Il percorso cambia fra gli ambienti (/ScreenSaver, /org/freedesktop/ScreenSaver): conta il mittente.
        var rule = new MatchRule { Type = MessageType.Signal, Sender = name, Interface = name, Member = "ActiveChanged" };
        Track(await bus.AddMatchAsync(rule,
            static (message, _) => message.GetBodyReader().ReadBool(),
            notification => RaiseIf(IsTrue(notification), SystemLockReason.SessionLocked),
            false, ObserverFlags.None, null).ConfigureAwait(false));
    }

    /// <summary>
    /// Percorso della sessione di logind. "auto" indica la sessione del processo o, se l'app non ne
    /// ha una (avvio da un servizio utente di systemd), la sessione grafica dell'utente.
    /// </summary>
    private static async Task<string> FindOwnSessionAsync(DBusConnection bus)
    {
        DBusErrorReplyException? lastError = null;
        foreach (var id in new[] { "auto", Environment.GetEnvironmentVariable("XDG_SESSION_ID") })
        {
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            try
            {
                return await bus.CallMethodAsync(GetSessionCall(bus, id),
                    static (message, _) => message.GetBodyReader().ReadObjectPathAsString(), null).ConfigureAwait(false);
            }
            catch (DBusErrorReplyException ex)
            {
                lastError = ex;
            }
        }

        throw lastError ?? (Exception)new InvalidOperationException("Sessione di logind non trovata.");
    }

    private static MessageBuffer GetSessionCall(DBusConnection bus, string id)
    {
        var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader(Login1, Login1Path, Login1Manager, "GetSession", "s", MessageFlags.None);
        writer.WriteString(id);
        return writer.CreateMessage();
    }

    // Le notifiche di connessione chiusa o di errore di lettura non bloccano: conta solo un valore vero.
    private static bool IsTrue(Notification<bool> notification) =>
        notification.Type == NotificationType.Value && notification.Value;

    private void Track(IDisposable watch)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _watches.Add(watch);
                return;
            }
        }

        watch.Dispose();
    }

    private void RaiseIf(bool condition, SystemLockReason reason)
    {
        if (condition && !_disposed)
        {
            LockRequired?.Invoke(this, reason);
        }
    }
}
