using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Kxc.App.Resources;
using Kxc.App.Services;
using Kxc.Core.Connections;
using Kxc.Core.Locking;
using Kxc.Core.Platform;
using Kxc.Core.Settings;
using Kxc.Core.Vault;

namespace Kxc.App.ViewModels;

/// <summary>
/// Navigazione tra primo avvio, sblocco, finestra principale e impostazioni; blocco per inattività,
/// con lo schermo del sistema, alla sospensione o su richiesta; ricarica quando il file cambia.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase, IDisposable
{
    /// <summary>Tentativi di ricarica mentre il file si sta sincronizzando.</summary>
    public static readonly TimeSpan ReloadRetryDelay = TimeSpan.FromSeconds(3);

    public const int ReloadAttempts = 5;

    private readonly IVaultService _vault;
    private readonly ISettingsStore _store;
    private readonly AppSettings _settings;
    private readonly IFileDialogService _files;
    private readonly ISystemSessionEvents _sessionEvents;
    private readonly TimeProvider _time;
    private readonly IConnectionLauncher _connections;
    private readonly IDatabaseWatcher _watcher;
    private readonly IKeePassXcCliLocator _cliLocator;
    private readonly ExecutableSafetyCheck _isSafeExecutable;
    private readonly InstallationStatus _installation;
    private readonly AutoLockTimer _autoLock;
    private MainViewModel? _main;
    private CancellationTokenSource? _session;
    private int _reloadGeneration;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    public ShellViewModel(
        IVaultService vault,
        ISettingsStore store,
        AppSettings settings,
        IFileDialogService files,
        ISystemSessionEvents sessionEvents,
        TimeProvider time,
        IConnectionLauncher connections,
        IDatabaseWatcher watcher,
        IKeePassXcCliLocator cliLocator,
        ExecutableSafetyCheck isSafeExecutable,
        InstallationStatus installation)
    {
        _vault = vault;
        _store = store;
        _settings = settings;
        _files = files;
        _sessionEvents = sessionEvents;
        _time = time;
        _connections = connections;
        _watcher = watcher;
        _cliLocator = cliLocator;
        _isSafeExecutable = isSafeExecutable;
        _installation = installation;

        _autoLock = new AutoLockTimer(time, settings.AutoLockTimeout);
        _autoLock.Tick += (_, remaining) => Dispatcher.UIThread.Post(() => UpdateLockCountdown(remaining));
        _autoLock.LockRequired += (_, _) => Dispatcher.UIThread.Post(() =>
            Lock(Messages.Format(Strings.Unlock_LockedByInactivity, (int)_settings.AutoLockTimeout.TotalMinutes)));
        _sessionEvents.LockRequired += (_, _) => Dispatcher.UIThread.Post(() => Lock(Strings.Unlock_LockedBySystem));
        _watcher.Changed += (_, _) => Dispatcher.UIThread.Post(() => _ = ReloadAsync());
    }

    public bool IsUnlocked => _main is not null;

    public void Start()
    {
        if (_settings.DatabasePath is { } path && File.Exists(path))
        {
            ShowUnlock();
        }
        else
        {
            CurrentPage = new FirstRunViewModel(_files, OnDatabaseChosen);
        }
    }

    /// <summary>Tasti e mouse dentro la finestra dell'app: solo questa attività rinvia il blocco.</summary>
    public void RegisterActivity()
    {
        if (_autoLock.IsRunning)
        {
            _autoLock.RegisterActivity();
        }
    }

    public void Lock(string? reason = null)
    {
        if (_main is null)
        {
            return;
        }

        _main = null;
        _reloadGeneration++;

        // Prima di tutto si fermano le connessioni ancora in avvio (S10).
        _session?.Cancel();
        _session?.Dispose();
        _session = null;

        _autoLock.Stop();
        _watcher.StopWatching();
        _vault.Lock();
        _connections.CleanUp();
        ShowUnlock(reason);
    }

    public void OpenSettings()
    {
        if (_main is null)
        {
            return;
        }

        CurrentPage = new SettingsViewModel(
            _settings,
            _store,
            _files,
            _connections,
            _cliLocator,
            path => _isSafeExecutable(path),
            back: () => CurrentPage = _main ?? CurrentPage,
            databaseChanged: () => Lock(Strings.Settings_DatabaseChanged),
            autoLockChanged: () =>
            {
                _autoLock.Timeout = _settings.AutoLockTimeout;
                UpdateLockCountdown(_autoLock.Remaining);
            });
    }

    /// <summary>
    /// FR-11: rilegge il database con le credenziali in memoria. Durante la sincronizzazione il file
    /// può essere incompleto: si ritenta; se le credenziali non valgono più, l'app si blocca.
    /// </summary>
    public async Task ReloadAsync()
    {
        if (_main is not { } main)
        {
            return;
        }

        var generation = ++_reloadGeneration;
        main.DatabaseStatus = Strings.Status_Reloading;
        for (var attempt = 1; attempt <= ReloadAttempts; attempt++)
        {
            try
            {
                var snapshot = await _vault.ReloadAsync(CancellationToken.None);
                if (generation != _reloadGeneration || _main != main)
                {
                    return;
                }

                main.ReplaceEntries(snapshot);
                main.DatabaseStatus = Messages.Format(Strings.Status_Reloaded, _time.GetLocalNow().ToString("t", System.Globalization.CultureInfo.CurrentCulture));
                return;
            }
            catch (VaultException ex) when (attempt < ReloadAttempts && ex.Error is not VaultError.Locked)
            {
                if (generation != _reloadGeneration)
                {
                    return;
                }

                main.DatabaseStatus = Strings.Status_ReloadRetrying;
                await Task.Delay(ReloadRetryDelay, _time);
            }
            catch (VaultException ex)
            {
                if (generation != _reloadGeneration || _main != main)
                {
                    return;
                }

                if (ex.Error == VaultError.InvalidCredentials)
                {
                    Lock(Strings.Status_CredentialsChanged);
                }
                else
                {
                    main.DatabaseStatus = Messages.Format(Strings.Status_ReloadFailed, Messages.For(ex.Error));
                }

                return;
            }
        }
    }

    public void Dispose()
    {
        _session?.Cancel();
        _session?.Dispose();
        _autoLock.Dispose();
        _watcher.Dispose();
        _sessionEvents.Dispose();
        _connections.CleanUp();
        if (_vault is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void OnDatabaseChosen(string path)
    {
        if (!string.Equals(_settings.DatabasePath, path, StringComparison.Ordinal))
        {
            _settings.DatabasePath = path;
            _store.Save(_settings);
        }

        ShowUnlock();
    }

    private void ShowUnlock(string? message = null)
    {
        CurrentPage = new UnlockViewModel(_vault, _store, _settings, _files, OnUnlocked)
        {
            InfoMessage = message,
            SecurityWarning = _installation.IsSafe ? null : Strings.Warning_UnsafeInstallation,
        };
    }

    private void OnUnlocked(VaultSnapshot snapshot)
    {
        _session = new CancellationTokenSource();
        _main = new MainViewModel(snapshot, _settings, _store, _time, _connections, () => Lock(), OpenSettings, _session.Token);
        CurrentPage = _main;
        _autoLock.Timeout = _settings.AutoLockTimeout;
        _autoLock.Start();
        UpdateLockCountdown(_autoLock.Remaining);
        if (_settings.DatabasePath is { } path)
        {
            _watcher.Watch(path);
        }
    }

    private void UpdateLockCountdown(TimeSpan remaining)
    {
        if (_main is { } main)
        {
            var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
            main.LockStatus = Strings.Status_Unlocked + " · " + (remaining < TimeSpan.FromMinutes(1)
                ? Strings.Status_LocksInUnderAMinute
                : Messages.Format(Strings.Status_LocksIn, minutes));
        }
    }
}
