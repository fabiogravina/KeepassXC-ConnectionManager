using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kxc.App.Services;
using Kxc.App.ViewModels;
using Kxc.App.Views;
using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Platform;
using Kxc.Core.Search;
using Kxc.Core.Settings;
using Kxc.Core.Vault;
using Kxc.Platform;
using Kxc.Platform.Settings;
using Kxc.Tests.TestData;
using Kxc.Tests.Vault;
using Kxc.Vault;
using Microsoft.Extensions.Time.Testing;

namespace Kxc.Tests.Ui;

/// <summary>
/// Flussi principali nell'interfaccia headless: primo avvio, sblocco, ricerca, preferiti e blocco.
/// Le schermate finiscono in bin/.../screenshots per la verifica visiva.
/// </summary>
public sealed class UiFlowTests : IDisposable
{
    private static readonly string ExportXml = File.ReadAllText(Fixture.XmlPath);
    private readonly string _configDirectory = Path.Combine(Path.GetTempPath(), "kxc-ui-" + Guid.NewGuid().ToString("N"));

    private sealed class FixedLocator : IKeePassXcCliLocator
    {
        public string? FindCli() => "keepassxc-cli";
    }

    private sealed class FakeFiles(string? database) : IFileDialogService
    {
        public string? Executable { get; set; }

        public Task<string?> PickDatabaseAsync() => Task.FromResult(database);

        public Task<string?> PickKeyFileAsync() => Task.FromResult<string?>(Fixture.KeyFilePath);

        public Task<string?> PickExecutableAsync() => Task.FromResult(Executable);
    }

    private sealed class FakeWatcher : IDatabaseWatcher
    {
        public event EventHandler? Changed;

        public string? Watching { get; private set; }

        public void Watch(string databasePath) => Watching = databasePath;

        public void StopWatching() => Watching = null;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
        }
    }

    /// <summary>keepassxc-cli simulato con export modificabile durante il test (per la ricarica).</summary>
    private sealed class Cli
    {
        public string ExportXml { get; set; } = UiFlowTests.ExportXml;

        public Queue<int> ExportExitCodes { get; } = new();

        public FakeProcessRunner Runner => new(request => request.Arguments[0] switch
        {
            "--version" => (0, "2.7.12\n", string.Empty),
            "export" when ExportExitCodes.TryDequeue(out var code) && code != 0 => (code, string.Empty, code == 1 ? string.Empty : "errore di lettura"),
            "export" => (0, ExportXml, string.Empty),
            "show" => (0, "segreto\n", string.Empty),
            _ => (2, string.Empty, "comando inatteso"),
        });
    }

    private sealed class FakeSessionEvents : ISystemSessionEvents
    {
        public event EventHandler<SystemLockReason>? LockRequired;

        public bool IsSupported => true;

        public void Raise() => LockRequired?.Invoke(this, SystemLockReason.SessionLocked);

        public void Dispose()
        {
        }
    }

    private sealed class FakeConnections : IConnectionLauncher
    {
        public List<(string Title, ConnectionProtocols Protocol)> Launched { get; } = [];

        public int CleanUps { get; private set; }

        public bool IsSupported(ConnectionProtocols protocol) => protocol == ConnectionProtocols.Rdp;

        public IReadOnlyList<ClientOption> Clients(ConnectionProtocols protocol) =>
            protocol == ConnectionProtocols.Rdp ? [new ClientOption("mstsc", "Desktop remoto (mstsc)")] : [];

        public ClientOption? ActiveClient(ConnectionProtocols protocol) => Clients(protocol) is [var first, ..] ? first : null;

        public Task LaunchAsync(ConnectionEntry entry, ConnectionProtocols protocol, CancellationToken cancellationToken)
        {
            Launched.Add((entry.Title, protocol));
            return Task.CompletedTask;
        }

        public void CleanUp() => CleanUps++;
    }

    private sealed record Harness(
        MainWindow Window, ShellViewModel Shell, AppSettings Settings, FakeTimeProvider Time, FakeSessionEvents Session,
        Cli Cli, FakeConnections Connections, FakeWatcher Watcher, FakeFiles Files);

    private Harness Create(AppSettings settings, int exportExitCode = 0)
    {
        var cli = new Cli();
        if (exportExitCode != 0)
        {
            for (var i = 0; i < 10; i++)
            {
                cli.ExportExitCodes.Enqueue(exportExitCode);
            }
        }

        var store = new JsonSettingsStore(new AppPaths(_configDirectory));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 17, 9, 0, 0, TimeSpan.Zero));
        var session = new FakeSessionEvents();
        var connections = new FakeConnections();
        var watcher = new FakeWatcher();
        var files = new FakeFiles(Fixture.DatabasePath);
        var shell = new ShellViewModel(
            new KeePassXcVaultService(cli.Runner, new FixedLocator()), store, settings, files, session, time, connections,
            watcher, new FixedLocator(), path => !path.Contains("insicuro", StringComparison.Ordinal));
        var window = new MainWindow { DataContext = shell, Width = 1120, Height = 720 };
        window.Show();
        shell.Start();
        Pump();
        return new Harness(window, shell, settings, time, session, cli, connections, watcher, files);
    }

    private static AppSettings UnlockableSettings() => new() { DatabasePath = Fixture.DatabasePath, KeyFilePath = Fixture.KeyFilePath };

    [AvaloniaFact]
    public void First_run_asks_only_for_the_database()
    {
        var h = Create(new AppSettings());
        Snapshot(h.Window, "1-primo-avvio");

        Assert.IsType<FirstRunViewModel>(h.Shell.CurrentPage);
        ((FirstRunViewModel)h.Shell.CurrentPage!).ChooseDatabaseCommand.Execute(null);
        PumpUntil(() => h.Shell.CurrentPage is UnlockViewModel);

        Assert.Equal(Fixture.DatabasePath, h.Settings.DatabasePath);
    }

    [AvaloniaFact]
    public void Enter_unlocks_and_shows_the_list()
    {
        var h = Create(UnlockableSettings());
        Snapshot(h.Window, "2-sblocco");

        var unlock = Assert.IsType<UnlockViewModel>(h.Shell.CurrentPage);
        Assert.True(Find<TextBox>(h.Window, "PasswordBox").IsFocused);
        h.Window.KeyTextInput("test");
        h.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        PumpUntil(() => h.Shell.CurrentPage is MainViewModel);

        var main = (MainViewModel)h.Shell.CurrentPage!;
        Assert.Equal(7, main.Items.Count);
        Assert.True(Find<TextBox>(h.Window, "SearchBox").IsFocused);
        Assert.Empty(unlock.Password);
        Snapshot(h.Window, "3-principale");
    }

    [AvaloniaFact]
    public void Wrong_password_shows_the_message_next_to_the_field()
    {
        var h = Create(UnlockableSettings(), exportExitCode: 1);

        h.Window.KeyTextInput("sbagliata");
        h.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        var unlock = (UnlockViewModel)h.Shell.CurrentPage!;
        PumpUntil(() => unlock.ErrorMessage is not null);

        Assert.Equal(Messages.For(VaultError.InvalidCredentials), unlock.ErrorMessage);
        Assert.Empty(unlock.Password);
        Snapshot(h.Window, "4-password-errata");
    }

    [AvaloniaFact]
    public void Typing_filters_arrows_move_and_escape_clears()
    {
        var h = UnlockedHarness();
        var main = (MainViewModel)h.Shell.CurrentPage!;

        h.Window.KeyTextInput("ross");
        Pump();
        // Quattro voci dei clienti Rossi più il Router, che ha «armadio rosso» nelle note.
        Assert.Equal(5, main.Items.Count);
        Assert.Equal("Router", main.Items[^1].Entry.Title);
        // Stesso punteggio (corrispondenza nella cartella): ordine alfabetico, preferiti ignorati.
        Assert.Equal("Contabilità", main.SelectedItem!.Entry.Title);
        Snapshot(h.Window, "5-ricerca");

        h.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Pump();
        Assert.Equal(main.Items[1], main.SelectedItem);

        h.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Pump();
        Assert.Empty(main.SearchText);
        Assert.Equal(7, main.Items.Count);
    }

    [AvaloniaFact]
    public void Search_is_global_and_choosing_a_filter_closes_it()
    {
        var h = UnlockedHarness();
        var main = (MainViewModel)h.Shell.CurrentPage!;
        main.SelectFilterCommand.Execute(main.FolderFilters.Single(f => f.Label == "Interni"));
        Pump();
        Assert.Equal(["Router"], main.Items.Select(i => i.Entry.Title));

        h.Window.KeyTextInput("gateway");
        Pump();
        Assert.Equal(EntryFilterKind.All, main.SelectedFilter!.Filter.Kind);
        Assert.True(main.QuickFilters[0].IsSelected);
        Assert.Equal("Gateway", Assert.Single(main.Items).Entry.Title);

        main.SelectFilterCommand.Execute(main.FolderFilters.Single(f => f.Label == "Interni"));
        Pump();
        Assert.Empty(main.SearchText);
        Assert.Equal(["Router"], main.Items.Select(i => i.Entry.Title));
    }

    [AvaloniaFact]
    public void Ctrl_d_marks_a_favorite_that_moves_to_the_top()
    {
        var h = UnlockedHarness();
        var main = (MainViewModel)h.Shell.CurrentPage!;
        main.SelectedItem = main.Items.Single(i => i.Entry.Title == "Router");

        h.Window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control);
        Pump();

        Assert.Contains(main.Items[0].Uuid, h.Settings.Favorites);
        Assert.Equal("Router", main.Items[0].Entry.Title);
        Assert.Equal(Kxc.App.Resources.Strings.Section_Favorites, main.Items[0].SectionHeader);
    }

    [AvaloniaFact]
    public void Enter_on_an_entry_without_host_explains_why()
    {
        var h = UnlockedHarness();
        var main = (MainViewModel)h.Shell.CurrentPage!;

        h.Window.KeyTextInput("senza host");
        Pump();
        h.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump();

        Assert.Contains("Senza host", main.ConnectionStatus, StringComparison.Ordinal);
        Snapshot(h.Window, "6-voce-senza-host");
    }

    [AvaloniaFact]
    public void Enter_starts_rdp_and_records_the_recent_connection()
    {
        var h = UnlockedHarness();
        var main = (MainViewModel)h.Shell.CurrentPage!;

        h.Window.KeyTextInput("server file");
        Pump();
        h.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        PumpUntil(() => h.Connections.Launched.Count == 1);

        Assert.Equal(("Server file", ConnectionProtocols.Rdp), h.Connections.Launched[0]);
        Assert.Equal(main.Items[0].Uuid, h.Settings.Recents[0].Uuid);
        Assert.Contains("RDP", main.ConnectionStatus, StringComparison.Ordinal);
        Snapshot(h.Window, "8-rdp-avviato");
    }

    [AvaloniaFact]
    public void Connect_buttons_are_named_after_the_protocol_in_list_and_detail()
    {
        var h = UnlockedHarness();
        var main = (MainViewModel)h.Shell.CurrentPage!;

        main.SelectedItem = main.Items.Single(i => i.Entry.Title == "Gateway");
        Pump();
        Snapshot(h.Window, "12-dettaglio-ssh-sftp");

        var detail = h.Window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Content is EntryDetailViewModel);
        var detailLabels = detail.GetVisualDescendants().OfType<Button>()
            .Select(b => b.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text)
            .ToList();
        Assert.Equal(["SSH", "SFTP"], detailLabels);

        var rdp = main.Items.Single(i => i.Entry.Title == "Server file");
        Assert.Equal(["RDP"], rdp.Protocols.Select(p => p.Label));
        Assert.DoesNotContain(main.Items.SelectMany(i => i.Protocols), p => p.Label == Kxc.App.Resources.Strings.Main_Connect);
    }

    [AvaloniaFact]
    public void Protocols_not_yet_available_are_explained()
    {
        var h = UnlockedHarness();
        var main = (MainViewModel)h.Shell.CurrentPage!;

        h.Window.KeyTextInput("gateway");
        Pump();
        h.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump();

        Assert.Empty(h.Connections.Launched);
        Assert.Contains("SSH", main.ConnectionStatus, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Host_key_dialog_shows_the_fingerprint_and_needs_an_explicit_trust()
    {
        var h = UnlockedHarness();
        var service = new HostKeyConfirmationService { Owner = h.Window };
        var question = new HostKeyQuestion("192.168.178.210", "ED25519", "SHA256:qskTGEszv5UB03SvrnElY66sYIDvN7Uuu7jU1GcwZ4s", "prompt");

        // Invio sul pulsante a fuoco (Annulla) non deve mai fidarsi dell'host.
        var cancelled = service.ConfirmAsync(question, CancellationToken.None);
        var dialog = OpenDialog(h.Window);
        Snapshot(dialog, "9-host-key");
        Assert.True(Find<Button>(dialog, "CancelButton").IsFocused);
        Assert.Contains(dialog.GetVisualDescendants().OfType<SelectableTextBlock>(), t => t.Text == question.Fingerprint);
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        PumpUntil(() => cancelled.IsCompleted);
        Assert.False(cancelled.Result);

        var trusted = service.ConfirmAsync(question, CancellationToken.None);
        Find<Button>(OpenDialog(h.Window), "TrustButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => trusted.IsCompleted);
        Assert.True(trusted.Result);
    }

    private static HostKeyDialog OpenDialog(Window owner)
    {
        PumpUntil(() => owner.OwnedWindows.OfType<HostKeyDialog>().Any());
        return owner.OwnedWindows.OfType<HostKeyDialog>().Single();
    }

    [AvaloniaFact]
    public void Ctrl_comma_opens_settings_and_escape_goes_back_to_the_same_list()
    {
        var h = UnlockedHarness();
        var main = (MainViewModel)h.Shell.CurrentPage!;
        h.Window.KeyTextInput("ross");
        Pump();

        h.Window.KeyPressQwerty(PhysicalKey.Comma, RawInputModifiers.Control);
        PumpUntil(() => h.Shell.CurrentPage is SettingsViewModel);
        Snapshot(h.Window, "10-impostazioni");

        h.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        PumpUntil(() => h.Shell.CurrentPage is MainViewModel);
        Assert.Same(main, h.Shell.CurrentPage);
        Assert.Equal("ross", main.SearchText);
    }

    [AvaloniaFact]
    public void Settings_are_saved_and_applied_at_once()
    {
        var h = UnlockedHarness();
        h.Shell.OpenSettings();
        var settings = (SettingsViewModel)h.Shell.CurrentPage!;

        settings.SelectedAutoLock = settings.AutoLockOptions.Single(o => (int)o.Value == 1);
        settings.SelectedTheme = settings.ThemeOptions.Single(o => (AppTheme)o.Value == AppTheme.Dark);
        settings.SelectedLanguage = settings.LanguageOptions.Single(o => (string)o.Value == "en");
        Pump();
        Snapshot(h.Window, "11-impostazioni-tema-scuro");

        var saved = new JsonSettingsStore(new AppPaths(_configDirectory)).Load();
        Assert.Equal(1, saved.AutoLockMinutes);
        Assert.Equal(AppTheme.Dark, saved.Theme);
        Assert.Equal("en", saved.Language);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, Avalonia.Application.Current!.RequestedThemeVariant);

        // Il nuovo tempo vale subito: un minuto dopo l'app è bloccata.
        h.Time.Advance(TimeSpan.FromMinutes(1));
        PumpUntil(() => h.Shell.CurrentPage is UnlockViewModel);
        SettingsViewModel.ApplyTheme(AppTheme.System);
    }

    [AvaloniaFact]
    public void Unsafe_keepassxc_cli_is_refused()
    {
        var h = UnlockedHarness();
        h.Shell.OpenSettings();
        var settings = (SettingsViewModel)h.Shell.CurrentPage!;

        h.Files.Executable = @"C:\insicuro\keepassxc-cli.exe";
        settings.ChangeCliCommand.Execute(null);
        PumpUntil(() => settings.CliError is not null);
        Assert.Null(h.Settings.KeePassXcCliPath);

        h.Files.Executable = @"C:\Program Files\KeePassXC\keepassxc-cli.exe";
        settings.ChangeCliCommand.Execute(null);
        PumpUntil(() => h.Settings.KeePassXcCliPath is not null);
        Assert.Null(settings.CliError);
    }

    [AvaloniaFact]
    public void Changing_the_database_in_settings_locks_and_asks_to_unlock_it()
    {
        var h = UnlockedHarness();
        h.Settings.DatabasePath = Fixture.Kdbx4DatabasePath;
        h.Shell.OpenSettings();
        var settings = (SettingsViewModel)h.Shell.CurrentPage!;

        settings.ChangeDatabaseCommand.Execute(null);
        PumpUntil(() => h.Shell.CurrentPage is UnlockViewModel);

        Assert.Equal(Fixture.DatabasePath, h.Settings.DatabasePath);
        Assert.Null(h.Watcher.Watching);
    }

    [AvaloniaFact]
    public void Changed_file_reloads_the_list_without_asking_the_password()
    {
        var h = UnlockedHarness();
        var main = (MainViewModel)h.Shell.CurrentPage!;
        Assert.Equal(Fixture.DatabasePath, h.Watcher.Watching);

        h.Cli.ExportXml = ExportXml.Replace("<Value>Router</Value>", "<Value>Router nuovo</Value>", StringComparison.Ordinal);
        h.Watcher.Raise();
        PumpUntil(() => main.Items.Any(i => i.Entry.Title == "Router nuovo"));

        Assert.Same(main, h.Shell.CurrentPage);
        Assert.Contains("09:00", main.DatabaseStatus, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void File_still_syncing_is_retried()
    {
        var h = UnlockedHarness();
        var main = (MainViewModel)h.Shell.CurrentPage!;
        h.Cli.ExportExitCodes.Enqueue(2);
        h.Cli.ExportExitCodes.Enqueue(2);
        h.Cli.ExportXml = ExportXml.Replace("<Value>NAS</Value>", "<Value>NAS 2</Value>", StringComparison.Ordinal);

        h.Watcher.Raise();
        PumpUntil(() => main.DatabaseStatus == Kxc.App.Resources.Strings.Status_ReloadRetrying);
        AdvanceUntil(h, () => main.Items.Any(e => e.Entry.Title == "NAS 2"));

        Assert.Same(main, h.Shell.CurrentPage);
        Assert.Empty(h.Cli.ExportExitCodes);
    }

    [AvaloniaFact]
    public void New_database_password_locks_the_app()
    {
        var h = UnlockedHarness();
        for (var i = 0; i < ShellViewModel.ReloadAttempts; i++)
        {
            h.Cli.ExportExitCodes.Enqueue(1);
        }

        h.Watcher.Raise();
        AdvanceUntil(h, () => h.Shell.CurrentPage is UnlockViewModel);

        Assert.Equal(Kxc.App.Resources.Strings.Status_CredentialsChanged, ((UnlockViewModel)h.Shell.CurrentPage!).InfoMessage);
    }

    [AvaloniaFact]
    public void Inactivity_locks_and_clears_credentials()
    {
        var h = UnlockedHarness();

        h.Time.Advance(TimeSpan.FromMinutes(5));
        PumpUntil(() => h.Shell.CurrentPage is UnlockViewModel);

        var unlock = (UnlockViewModel)h.Shell.CurrentPage!;
        Assert.Contains("5", unlock.InfoMessage, StringComparison.Ordinal);
        Assert.Equal(1, h.Connections.CleanUps);
        Snapshot(h.Window, "7-bloccato-inattivita");
    }

    [AvaloniaFact]
    public void Activity_in_the_window_postpones_the_lock()
    {
        var h = UnlockedHarness();

        h.Time.Advance(TimeSpan.FromMinutes(4));
        h.Window.KeyTextInput("r");
        h.Time.Advance(TimeSpan.FromMinutes(4));
        Pump();

        Assert.IsType<MainViewModel>(h.Shell.CurrentPage);
        Assert.Contains("1 min", ((MainViewModel)h.Shell.CurrentPage!).LockStatus, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void System_lock_and_ctrl_l_lock_the_app()
    {
        var h = UnlockedHarness();
        h.Session.Raise();
        PumpUntil(() => h.Shell.CurrentPage is UnlockViewModel);

        h.Window.KeyTextInput("test");
        h.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        PumpUntil(() => h.Shell.CurrentPage is MainViewModel);
        h.Window.KeyPressQwerty(PhysicalKey.L, RawInputModifiers.Control);
        PumpUntil(() => h.Shell.CurrentPage is UnlockViewModel);
    }

    private Harness UnlockedHarness()
    {
        var h = Create(UnlockableSettings());
        h.Window.KeyTextInput("test");
        h.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        PumpUntil(() => h.Shell.CurrentPage is MainViewModel);
        return h;
    }

    private static T Find<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static void Pump()
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    /// <summary>Pompa la coda dell'interfaccia e avanza l'orologio simulato a passi di un secondo.</summary>
    private static void AdvanceUntil(Harness h, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condizione non raggiunta entro 10 secondi.");
            Pump();
            Thread.Sleep(10);
            Pump();
            h.Time.Advance(TimeSpan.FromSeconds(1));
        }

        Pump();
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condizione non raggiunta entro 10 secondi.");
            Pump();
            Thread.Sleep(10);
        }

        Pump();
    }

    private static void Snapshot(Window window, string name)
    {
        Pump();
        var directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
#pragma warning disable CS0618 // Il formato PNG predefinito basta per le schermate dei test.
        frame?.Save(Path.Combine(directory, name + ".png"));
#pragma warning restore CS0618
    }

    public void Dispose()
    {
        if (Directory.Exists(_configDirectory))
        {
            Directory.Delete(_configDirectory, recursive: true);
        }
    }
}
