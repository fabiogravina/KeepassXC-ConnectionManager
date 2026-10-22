using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kxc.App.Resources;
using Kxc.App.Services;
using Kxc.Core.Security;
using Kxc.Core.Settings;
using Kxc.Core.Vault;

namespace Kxc.App.ViewModels;

/// <summary>
/// Sblocco con password, key file o entrambi. Il percorso del key file si ricorda (FR-02), la
/// password mai: il campo viene svuotato a ogni tentativo.
/// </summary>
public sealed partial class UnlockViewModel : ViewModelBase
{
    private readonly IVaultService _vault;
    private readonly ISettingsStore _store;
    private readonly AppSettings _settings;
    private readonly IFileDialogService _files;
    private readonly Action<VaultSnapshot> _onUnlocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DatabaseFileName), nameof(NetworkWarning))]
    private string _databasePath;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevealButtonText))]
    private bool _isPasswordVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyFileName), nameof(HasKeyFile), nameof(KeyFileButtonText), nameof(NetworkWarning))]
    private string? _keyFilePath;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _infoMessage;

    /// <summary>Avviso sull'installazione (S5), mostrato prima che si digiti la password.</summary>
    [ObservableProperty]
    private string? _securityWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnlockButtonText))]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool _isBusy;

    public UnlockViewModel(IVaultService vault, ISettingsStore store, AppSettings settings, IFileDialogService files, Action<VaultSnapshot> onUnlocked)
    {
        _vault = vault;
        _store = store;
        _settings = settings;
        _files = files;
        _onUnlocked = onUnlocked;
        _databasePath = settings.DatabasePath ?? string.Empty;
        _keyFilePath = settings.KeyFilePath;
    }

    /// <summary>Chiesto dalla vista per riportare il focus sul campo password.</summary>
    public event EventHandler? FocusPasswordRequested;

    public string DatabaseFileName => Path.GetFileName(DatabasePath);

    /// <summary>S13: database o key file su un percorso di rete.</summary>
    public string? NetworkWarning =>
        NetworkPaths.IsNetworkPath(DatabasePath) || NetworkPaths.IsNetworkPath(KeyFilePath) ? Strings.Warning_NetworkPath : null;

    public string? KeyFileName => KeyFilePath is null ? null : Path.GetFileName(KeyFilePath);

    public bool HasKeyFile => KeyFilePath is not null;

    public string KeyFileButtonText => HasKeyFile ? Strings.Unlock_KeyFileChange : Strings.Unlock_KeyFileChoose;

    public string RevealButtonText => IsPasswordVisible ? Strings.Unlock_Hide : Strings.Unlock_Show;

    public string UnlockButtonText => IsBusy ? Strings.Unlock_Busy : Strings.Unlock_Button;

    private bool CanUnlock => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        ErrorMessage = null;
        InfoMessage = null;

        // La stringa del campo non si può azzerare (limite di SEC-02): si copia subito in un buffer
        // azzerabile e il campo si svuota.
        var password = SecretBuffer.FromChars(Password);
        Password = string.Empty;

        if (password.IsEmpty && KeyFilePath is null)
        {
            password.Dispose();
            ErrorMessage = Strings.Unlock_PasswordRequired;
            FocusPasswordRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsBusy = true;
        try
        {
            var credentials = new UnlockCredentials(DatabasePath, password, KeyFilePath);
            var snapshot = await _vault.UnlockAsync(credentials, CancellationToken.None);

            if (!string.Equals(_settings.KeyFilePath, KeyFilePath, StringComparison.Ordinal))
            {
                _settings.KeyFilePath = KeyFilePath;
                _store.Save(_settings);
            }

            _onUnlocked(snapshot);
        }
        catch (VaultException ex)
        {
            ErrorMessage = Messages.For(ex.Error);
            FocusPasswordRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    private async Task ChangeDatabaseAsync()
    {
        if (await _files.PickDatabaseAsync() is { } path)
        {
            DatabasePath = path;
            ErrorMessage = null;
            _settings.DatabasePath = path;
            _store.Save(_settings);
        }

        FocusPasswordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ChooseKeyFileAsync()
    {
        if (await _files.PickKeyFileAsync() is { } path)
        {
            KeyFilePath = path;
            ErrorMessage = null;
        }

        FocusPasswordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemoveKeyFile()
    {
        KeyFilePath = null;
        FocusPasswordRequested?.Invoke(this, EventArgs.Empty);
    }
}
