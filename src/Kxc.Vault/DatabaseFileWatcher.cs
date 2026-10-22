using Kxc.Core.Vault;

namespace Kxc.Vault;

/// <summary>
/// Osservazione del database sincronizzato su Google Drive o OneDrive. Si osserva la cartella e non
/// il file, perché KeePassXC salva con un rename; in più un controllo periodico copre i filesystem
/// virtuali che non generano eventi. Il cambio si segnala solo quando dimensione e data restano
/// uguali per un intervallo, cioè quando la sincronizzazione ha finito di scrivere.
/// </summary>
public sealed class DatabaseFileWatcher : IDatabaseWatcher
{
    public static readonly TimeSpan StableFor = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private ITimer? _poll;
    private ITimer? _settle;
    private string? _path;
    private FileState? _loaded;
    private FileState? _pending;

    public DatabaseFileWatcher(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    public event EventHandler? Changed;

    public void Watch(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        StopWatching();

        var full = Path.GetFullPath(databasePath);
        lock (_gate)
        {
            _path = full;
            _loaded = FileState.Read(full);
            _pending = null;
            _poll = _time.CreateTimer(_ => ScheduleCheck(), null, PollInterval, PollInterval);
        }

        try
        {
            var watcher = new FileSystemWatcher(Path.GetDirectoryName(full)!, Path.GetFileName(full))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
            };
            watcher.Changed += (_, _) => ScheduleCheck();
            watcher.Created += (_, _) => ScheduleCheck();
            watcher.Renamed += (_, _) => ScheduleCheck();
            watcher.Deleted += (_, _) => ScheduleCheck();
            watcher.EnableRaisingEvents = true;
            lock (_gate)
            {
                _watcher = watcher;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
        {
            // Cartella non osservabile (unità di rete, filesystem virtuale): resta il controllo periodico.
        }
    }

    public void StopWatching()
    {
        lock (_gate)
        {
            _watcher?.Dispose();
            _watcher = null;
            _poll?.Dispose();
            _poll = null;
            _settle?.Dispose();
            _settle = null;
            _path = null;
        }
    }

    public void Dispose() => StopWatching();

    /// <summary>Ogni evento rinvia il controllo: si guarda il file solo dopo un intervallo di quiete.</summary>
    private void ScheduleCheck()
    {
        lock (_gate)
        {
            if (_path is null)
            {
                return;
            }

            _settle?.Dispose();
            _settle = _time.CreateTimer(_ => Check(), null, StableFor, Timeout.InfiniteTimeSpan);
        }
    }

    private void Check()
    {
        bool changed;
        lock (_gate)
        {
            if (_path is null)
            {
                return;
            }

            var current = FileState.Read(_path);
            if (current is null || current == _loaded)
            {
                // File assente durante il rename o invariato.
                _pending = null;
                return;
            }

            if (current != _pending)
            {
                // Ancora in scrittura: si riguarda dopo un altro intervallo.
                _pending = current;
                _settle?.Dispose();
                _settle = _time.CreateTimer(_ => Check(), null, StableFor, Timeout.InfiniteTimeSpan);
                return;
            }

            _loaded = current;
            _pending = null;
            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed record FileState(long Length, DateTime LastWriteUtc)
    {
        public static FileState? Read(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? new FileState(info.Length, info.LastWriteTimeUtc) : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
