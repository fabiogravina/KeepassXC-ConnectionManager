using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Kxc.App.Services;
using Kxc.App.ViewModels;
using Kxc.App.Views;
using Kxc.Core.Connections;
using Kxc.Core.Platform;
using Kxc.Core.Settings;
using Kxc.Core.Vault;
using Microsoft.Extensions.DependencyInjection;

namespace Kxc.App;

public sealed partial class App : Application, IDisposable
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        _services = AppServices.Build();
        ApplyLanguage(_services.GetRequiredService<AppSettings>());

        // Dopo la lingua: le stringhe delle viste si leggono al caricamento.
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (_services is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = _services.GetRequiredService<AppSettings>();
            SettingsViewModel.ApplyTheme(settings.Theme);

            // S7: credenziali RDP dell'app rimaste da una sessione chiusa male.
            _services.GetService<ISystemCredentialStore>()?.RemoveLeftovers();

            var shell = _services.GetRequiredService<ShellViewModel>();
            var window = new MainWindow { DataContext = shell };
            var store = _services.GetRequiredService<ISettingsStore>();
            WindowPlacementService.Restore(window, settings);
            window.Closing += (_, _) => WindowPlacementService.Save(window, settings, store);
            _services.GetRequiredService<FileDialogService>().Owner = window;
            _services.GetRequiredService<HostKeyConfirmationService>().Owner = window;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => Dispose();
            HandleUnexpectedErrors(shell);
            shell.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void Dispose()
    {
        // Azzera le credenziali in memoria anche alla chiusura.
        _services?.Dispose();
        _services = null;
    }

    /// <summary>
    /// S12: un errore imprevisto sul thread dell'interfaccia blocca l'app (credenziali azzerate)
    /// invece di chiuderla; un crash su un altro thread azzera comunque ciò che può prima di uscire.
    /// </summary>
    private void HandleUnexpectedErrors(ShellViewModel shell)
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            shell.Lock(Kxc.App.Resources.Strings.Error_UnexpectedLocked);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            try
            {
                _services?.GetService<IVaultService>()?.Lock();
                _services?.GetService<IConnectionLauncher>()?.CleanUp();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                // Il processo sta terminando: si fa quel che si può.
            }
        };
    }

    private static void ApplyLanguage(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Language))
        {
            return;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(settings.Language);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Lingua non valida nelle impostazioni: si resta su quella del sistema.
        }
    }
}
