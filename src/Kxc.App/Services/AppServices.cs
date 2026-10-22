using System.Runtime.Versioning;
using Kxc.App.ViewModels;
using Kxc.Connectors;
using Kxc.Connectors.Rdp;
using Kxc.Connectors.Sftp;
using Kxc.Connectors.Ssh;
using Kxc.Core.Connections;
using Kxc.Core.Entries;
using Kxc.Core.Platform;
using Kxc.Core.Settings;
using Kxc.Core.Vault;
using Kxc.Platform;
using Kxc.Platform.Credentials;
using Kxc.Platform.Executables;
using Kxc.Platform.Network;
using Kxc.Platform.Processes;
using Kxc.Platform.Session;
using Kxc.Platform.Settings;
using Kxc.Vault;
using Microsoft.Extensions.DependencyInjection;

namespace Kxc.App.Services;

/// <summary>Composizione delle dipendenze: l'unico punto che conosce le implementazioni.</summary>
internal static class AppServices
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AppPaths>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton(sp => sp.GetRequiredService<ISettingsStore>().Load());

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IKeePassXcCliLocator>(sp =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            return new KeePassXcCliLocator(() => settings.KeePassXcCliPath);
        });
        services.AddSingleton<IVaultService, KeePassXcVaultService>();
        services.AddSingleton(_ => SystemSessionEvents.Create());
        services.AddSingleton<IDatabaseWatcher, DatabaseFileWatcher>();
        services.AddSingleton<ExecutableSafetyCheck>(ExecutableSafety.IsSafe);
        services.AddSingleton(sp => InstallationStatus.Check(sp.GetRequiredService<ExecutableSafetyCheck>(), InstallationStatus.AppExecutables()));

        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<ITcpConnectionProbe, TcpConnectionProbe>();
        services.AddSingleton<HostKeyConfirmationService>();
        services.AddSingleton<IHostKeyConfirmation>(sp => sp.GetRequiredService<HostKeyConfirmationService>());

        if (OperatingSystem.IsWindows())
        {
            AddWindowsConnectors(services);
        }

        services.AddSingleton<IConnectionLauncher>(sp =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            return new ConnectionLauncher(sp.GetServices<IConnector>(), settings.PreferredClient);
        });

        services.AddSingleton<FileDialogService>();
        services.AddSingleton<IFileDialogService>(sp => sp.GetRequiredService<FileDialogService>());
        services.AddSingleton<ShellViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>Connettori verificati su Windows nella fase 0: mstsc, OpenSSH in Windows Terminal, WinSCP.</summary>
    [SupportedOSPlatform("windows")]
    private static void AddWindowsConnectors(ServiceCollection services)
    {
        // L'helper di SSH_ASKPASS è distribuito accanto all'app.
        var askPass = Path.Combine(AppContext.BaseDirectory, "kxc-askpass.exe");
        var winScp = WinScpConnector.Find(ExecutableSafety.IsSafe);

        // L'ordine di registrazione è l'ordine di preferenza predefinito per ciascun protocollo.
        services.AddSingleton<IConnector>(sp => OpenSsh(sp, ConnectionProtocols.Ssh, askPass, OpenSshTerminal.WindowsTerminal));
        services.AddSingleton<IConnector>(sp => OpenSsh(sp, ConnectionProtocols.Ssh, askPass, OpenSshTerminal.Console));

        // SFTP: WinSCP se installato, altrimenti sftp di OpenSSH.
        if (winScp is not null)
        {
            services.AddSingleton<IConnector>(sp => new WinScpConnector(sp.GetRequiredService<IVaultService>(), sp.GetRequiredService<IProcessLauncher>(), winScp));
        }

        services.AddSingleton<IConnector>(sp => OpenSsh(sp, ConnectionProtocols.Sftp, askPass, OpenSshTerminal.WindowsTerminal));
        services.AddSingleton<IConnector>(sp => OpenSsh(sp, ConnectionProtocols.Sftp, askPass, OpenSshTerminal.Console));

        services.AddSingleton<ISystemCredentialStore, WindowsCredentialStore>();
        services.AddSingleton<IConnector>(sp => new WindowsRdpConnector(
            sp.GetRequiredService<IVaultService>(),
            sp.GetRequiredService<ISystemCredentialStore>(),
            sp.GetRequiredService<IProcessLauncher>(),
            sp.GetRequiredService<ITcpConnectionProbe>(),
            sp.GetRequiredService<TimeProvider>(),
            isSafeExecutable: ExecutableSafety.IsSafe));
    }

    private static WindowsOpenSshConnector OpenSsh(IServiceProvider sp, ConnectionProtocols protocol, string askPass, OpenSshTerminal terminal) => new(
        protocol,
        sp.GetRequiredService<IVaultService>(),
        sp.GetRequiredService<IProcessLauncher>(),
        sp.GetRequiredService<IHostKeyConfirmation>(),
        askPass,
        terminal: terminal,
        isSafeExecutable: ExecutableSafety.IsSafe);
}
