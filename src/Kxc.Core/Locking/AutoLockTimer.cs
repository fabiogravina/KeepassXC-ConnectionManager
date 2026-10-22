namespace Kxc.Core.Locking;

/// <summary>
/// Blocco dopo inattività dentro KXC-ConnectionManager (SEC-03). L'attività nelle sessioni avviate
/// non conta: le segnala solo l'interfaccia dell'app con <see cref="RegisterActivity"/>.
/// </summary>
public sealed class AutoLockTimer : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private DateTimeOffset _lastActivity;
    private TimeSpan _timeout;

    public AutoLockTimer(TimeProvider time, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        Timeout = timeout;
    }

    /// <summary>Scatta una sola volta per ogni avvio; poi il timer si ferma.</summary>
    public event EventHandler? LockRequired;

    /// <summary>Scatta ogni secondo mentre il timer è attivo, per aggiornare la barra di stato.</summary>
    public event EventHandler<TimeSpan>? Tick;

    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _timeout = value;
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _timer is not null;
            }
        }
    }

    public TimeSpan Remaining
    {
        get
        {
            lock (_gate)
            {
                var left = _timeout - (_time.GetUtcNow() - _lastActivity);
                return left < TimeSpan.Zero ? TimeSpan.Zero : left;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            _lastActivity = _time.GetUtcNow();
            _timer ??= _time.CreateTimer(_ => OnTick(), null, TickInterval, TickInterval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void RegisterActivity()
    {
        lock (_gate)
        {
            _lastActivity = _time.GetUtcNow();
        }
    }

    public void Dispose() => Stop();

    private void OnTick()
    {
        var remaining = Remaining;
        if (remaining > TimeSpan.Zero)
        {
            Tick?.Invoke(this, remaining);
            return;
        }

        lock (_gate)
        {
            if (_timer is null)
            {
                return;
            }

            _timer.Dispose();
            _timer = null;
        }

        LockRequired?.Invoke(this, EventArgs.Empty);
    }
}
