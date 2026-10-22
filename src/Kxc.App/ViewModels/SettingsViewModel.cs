using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kxc.App.Resources;
using Kxc.App.Services;
using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Settings;
using Kxc.Core.Vault;

namespace Kxc.App.ViewModels;

/// <summary>Voce di una tendina: valore salvato e testo mostrato.</summary>
public sealed record ChoiceItem(object Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// FR-12: una sola pagina con quattro gruppi (database, client, sicurezza, aspetto). Ogni modifica si
/// salva subito; tema e blocco automatico si applicano subito, la lingua al prossimo avvio.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    public static readonly IReadOnlyList<int> AutoLockChoices = [1, 2, 5, 10, 15, 30, 60];

    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly IFileDialogService _files;
    private readonly IKeePassXcCliLocator _cliLocator;
    private readonly Func<string, bool> _isSafeExecutable;
    private readonly Action _back;
    private readonly Action _databaseChanged;
    private readonly Action _autoLockChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DatabaseFileName))]
    private string? _databasePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyFileName), nameof(HasKeyFile))]
    private string? _keyFilePath;

    [ObservableProperty]
    private string _cliDisplay = string.Empty;

    [ObservableProperty]
    private string? _cliError;

    [ObservableProperty]
    private ChoiceItem _selectedAutoLock;

    [ObservableProperty]
    private ChoiceItem _selectedTheme;

    [ObservableProperty]
    private ChoiceItem _selectedLanguage;

    public SettingsViewModel(
        AppSettings settings,
        ISettingsStore store,
        IFileDialogService files,
        IConnectionLauncher connections,
        IKeePassXcCliLocator cliLocator,
        Func<string, bool> isSafeExecutable,
        Action back,
        Action databaseChanged,
        Action autoLockChanged)
    {
        ArgumentNullException.ThrowIfNull(connections);
        _settings = settings;
        _store = store;
        _files = files;
        _cliLocator = cliLocator;
        _isSafeExecutable = isSafeExecutable;
        _back = back;
        _databaseChanged = databaseChanged;
        _autoLockChanged = autoLockChanged;

        _databasePath = settings.DatabasePath;
        _keyFilePath = settings.KeyFilePath;

        Clients =
        [
            new ClientChoiceViewModel(ConnectionProtocols.Rdp, connections, settings, store, Strings.Settings_InstallRdp),
            new ClientChoiceViewModel(ConnectionProtocols.Ssh, connections, settings, store, Strings.Settings_InstallSsh),
            new ClientChoiceViewModel(ConnectionProtocols.Sftp, connections, settings, store, Strings.Settings_InstallSftp),
        ];

        AutoLockOptions = AutoLockChoices
            .Select(m => new ChoiceItem(m, m == 1 ? Strings.Settings_AfterOneMinute : Messages.Format(Strings.Settings_AfterMinutes, m)))
            .ToList();
        _selectedAutoLock = AutoLockOptions.FirstOrDefault(o => (int)o.Value == settings.AutoLockMinutes)
            ?? AutoLockOptions.First(o => (int)o.Value == AppSettings.DefaultAutoLockMinutes);

        ThemeOptions =
        [
            new ChoiceItem(AppTheme.System, Strings.Theme_System),
            new ChoiceItem(AppTheme.Light, Strings.Theme_Light),
            new ChoiceItem(AppTheme.Dark, Strings.Theme_Dark),
        ];
        _selectedTheme = ThemeOptions.First(o => (AppTheme)o.Value == settings.Theme);

        LanguageOptions =
        [
            new ChoiceItem(string.Empty, Strings.Language_System),
            new ChoiceItem("it", "Italiano"),
            new ChoiceItem("en", "English"),
        ];
        _selectedLanguage = LanguageOptions.FirstOrDefault(o => (string)o.Value == (settings.Language ?? string.Empty)) ?? LanguageOptions[0];

        UpdateCliDisplay();
    }

    public string? DatabaseFileName => DatabasePath is null ? null : Path.GetFileName(DatabasePath);

    public string? KeyFileName => KeyFilePath is null ? null : Path.GetFileName(KeyFilePath);

    public bool HasKeyFile => KeyFilePath is not null;

    public IReadOnlyList<ClientChoiceViewModel> Clients { get; }

    public IReadOnlyList<ChoiceItem> AutoLockOptions { get; }

    public IReadOnlyList<ChoiceItem> ThemeOptions { get; }

    public IReadOnlyList<ChoiceItem> LanguageOptions { get; }

    public bool HasConfiguredCli => _settings.KeePassXcCliPath is not null;

    partial void OnSelectedAutoLockChanged(ChoiceItem value)
    {
        _settings.AutoLockMinutes = (int)value.Value;
        _store.Save(_settings);
        _autoLockChanged();
    }

    partial void OnSelectedThemeChanged(ChoiceItem value)
    {
        _settings.Theme = (AppTheme)value.Value;
        _store.Save(_settings);
        ApplyTheme(_settings.Theme);
    }

    partial void OnSelectedLanguageChanged(ChoiceItem value)
    {
        var language = (string)value.Value;
        _settings.Language = language.Length == 0 ? null : language;
        _store.Save(_settings);
    }

    public static void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    [RelayCommand]
    private void Back() => _back();

    /// <summary>Un altro database va sbloccato: l'app si blocca e mostra lo sblocco.</summary>
    [RelayCommand]
    private async Task ChangeDatabaseAsync()
    {
        if (await _files.PickDatabaseAsync() is { } path && !string.Equals(path, _settings.DatabasePath, StringComparison.Ordinal))
        {
            DatabasePath = path;
            _settings.DatabasePath = path;
            _store.Save(_settings);
            _databaseChanged();
        }
    }

    [RelayCommand]
    private async Task ChangeKeyFileAsync()
    {
        if (await _files.PickKeyFileAsync() is { } path)
        {
            KeyFilePath = path;
            _settings.KeyFilePath = path;
            _store.Save(_settings);
        }
    }

    [RelayCommand]
    private void RemoveKeyFile()
    {
        KeyFilePath = null;
        _settings.KeyFilePath = null;
        _store.Save(_settings);
    }

    [RelayCommand]
    private async Task ChangeCliAsync()
    {
        if (await _files.PickExecutableAsync() is not { } path)
        {
            return;
        }

        if (!_isSafeExecutable(path))
        {
            CliError = Strings.Settings_CliUnsafe;
            return;
        }

        CliError = null;
        _settings.KeePassXcCliPath = path;
        _store.Save(_settings);
        UpdateCliDisplay();
    }

    [RelayCommand]
    private void ResetCli()
    {
        CliError = null;
        _settings.KeePassXcCliPath = null;
        _store.Save(_settings);
        UpdateCliDisplay();
    }

    private void UpdateCliDisplay()
    {
        var found = _cliLocator.FindCli();
        CliDisplay = found is null
            ? Strings.Error_CliNotFound
            : _settings.KeePassXcCliPath is null ? found + " (" + Strings.Settings_CliAuto + ")" : found;
        OnPropertyChanged(nameof(HasConfiguredCli));
    }
}

/// <summary>Client di un protocollo: solo quelli rilevati nel sistema; se nessuno, cosa installare.</summary>
public sealed partial class ClientChoiceViewModel : ViewModelBase
{
    private readonly ConnectionProtocols _protocol;
    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;

    [ObservableProperty]
    private ChoiceItem? _selected;

    public ClientChoiceViewModel(ConnectionProtocols protocol, IConnectionLauncher connections, AppSettings settings, ISettingsStore store, string installHint)
    {
        _protocol = protocol;
        _settings = settings;
        _store = store;
        ProtocolName = Messages.ProtocolName(protocol);
        Options = connections.Clients(protocol).Select(c => new ChoiceItem(c.Id, c.Name)).ToList();
        var active = connections.ActiveClient(protocol);
        _selected = Options.FirstOrDefault(o => (string)o.Value == active?.Id);
        MissingText = Messages.Format(Strings.Settings_ClientMissing, installHint);
    }

    public string ProtocolName { get; }

    public IReadOnlyList<ChoiceItem> Options { get; }

    public bool IsAvailable => Options.Count > 0;

    public string Status => IsAvailable ? Strings.Settings_ClientDetected : MissingText;

    public string MissingText { get; }

    partial void OnSelectedChanged(ChoiceItem? value)
    {
        if (value is not null)
        {
            _settings.SetPreferredClient(_protocol, (string)value.Value);
            _store.Save(_settings);
        }
    }
}
