using Kxc.Core.Settings;
using Kxc.Platform;
using Kxc.Platform.Executables;
using Kxc.Platform.Settings;

namespace Kxc.Tests.Platform;

public sealed class PlatformTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kxc-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Settings_round_trip_without_secrets()
    {
        var store = new JsonSettingsStore(new AppPaths(_directory));
        var favorite = Guid.NewGuid();
        var settings = new AppSettings { DatabasePath = "D:\\db.kdbx", AutoLockMinutes = 10, Theme = AppTheme.Dark };
        settings.ToggleFavorite(favorite);

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("D:\\db.kdbx", loaded.DatabasePath);
        Assert.Equal(10, loaded.AutoLockMinutes);
        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.Equal([favorite], loaded.Favorites);
        Assert.DoesNotContain("Password", File.ReadAllText(Path.Combine(_directory, "settings.json")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Corrupted_settings_fall_back_to_defaults_and_keep_a_copy()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{ non è json");

        var loaded = new JsonSettingsStore(new AppPaths(_directory)).Load();

        Assert.Equal(AppSettings.DefaultAutoLockMinutes, loaded.AutoLockMinutes);
        Assert.True(File.Exists(Path.Combine(_directory, "settings.json.bak")));
    }

    [Fact]
    public void Recents_keep_the_latest_first_without_duplicates()
    {
        var settings = new AppSettings();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;

        settings.AddRecent(a, t0);
        settings.AddRecent(b, t0.AddMinutes(1));
        settings.AddRecent(a, t0.AddMinutes(2));

        Assert.Equal([a, b], settings.Recents.Select(r => r.Uuid));
    }

    [Fact]
    public void Configured_cli_path_is_used_only_if_safe()
    {
        Assert.Equal("cli", new KeePassXcCliLocator(() => "cli", _ => true).FindCli());
        Assert.Null(new KeePassXcCliLocator(() => "cli", _ => false).FindCli());
    }

    [Fact]
    public void S5_installation_check_lists_only_existing_unsafe_files()
    {
        Directory.CreateDirectory(_directory);
        var app = Path.Combine(_directory, "app.exe");
        var helper = Path.Combine(_directory, "helper.exe");
        File.WriteAllText(app, string.Empty);
        File.WriteAllText(helper, string.Empty);

        var status = Kxc.App.Services.InstallationStatus.Check(path => path != helper, [app, helper, Path.Combine(_directory, "manca.exe"), null]);

        Assert.False(status.IsSafe);
        Assert.Equal([helper], status.UnsafePaths);
        Assert.True(Kxc.App.Services.InstallationStatus.Check(_ => true, [app]).IsSafe);
    }

    [Fact]
    public void S8_executable_in_a_private_folder_is_safe()
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, "tool.exe");
        File.WriteAllText(file, string.Empty);

        Assert.True(ExecutableSafety.IsSafe(file));
    }

    [Fact]
    public void S8_any_other_principal_with_write_rights_makes_it_unsafe()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "ACL: solo Windows");
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, "tool.exe");
        File.WriteAllText(file, string.Empty);

        // Un utente qualsiasi (SID inventato), non uno dei gruppi «larghi» controllati prima di S8.
        var info = new FileInfo(file);
        var security = info.GetAccessControl();
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier("S-1-5-21-1111111111-2222222222-3333333333-1001"),
            System.Security.AccessControl.FileSystemRights.WriteData,
            System.Security.AccessControl.AccessControlType.Allow));
        info.SetAccessControl(security);

        Assert.False(ExecutableSafety.IsSafe(file));
    }

    [Fact]
    public void S8_parent_folder_that_others_can_rename_makes_it_unsafe()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "ACL: solo Windows");
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var inner = Path.Combine(_directory, "app");
        Directory.CreateDirectory(inner);
        var file = Path.Combine(inner, "tool.exe");
        File.WriteAllText(file, string.Empty);
        Assert.True(ExecutableSafety.IsSafe(file));

        // Chi può cancellare (rinominare) la cartella superiore può sostituire tutta la cartella dell'app.
        var parent = new DirectoryInfo(_directory);
        var security = parent.GetAccessControl();
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
            System.Security.AccessControl.FileSystemRights.Delete,
            System.Security.AccessControl.AccessControlType.Allow));
        parent.SetAccessControl(security);

        Assert.False(ExecutableSafety.IsSafe(file));
    }

    [Theory]
    [InlineData(@"\\nas\backup\database.kdbx", true)]
    [InlineData(@"\\?\UNC\nas\backup\database.kdbx", true)]
    [InlineData(@"C:\Users\fabio\database.kdbx", false)]
    [InlineData(@"\\?\C:\Users\fabio\database.kdbx", false)]
    [InlineData("", false)]
    public void S13_network_paths_are_recognized(string path, bool network)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Percorsi UNC: solo Windows");
        Assert.Equal(network, Kxc.App.Services.NetworkPaths.IsNetworkPath(path));
    }

    [Fact]
    public void Missing_executable_is_not_safe()
    {
        Assert.False(ExecutableSafety.IsSafe(Path.Combine(_directory, "manca.exe")));
    }

    [Fact]
    public void Executable_writable_by_everyone_is_not_safe()
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, "tool");
        File.WriteAllText(file, string.Empty);

        if (OperatingSystem.IsWindows())
        {
            var info = new FileInfo(file);
            var security = info.GetAccessControl();
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                System.Security.AccessControl.FileSystemRights.Modify,
                System.Security.AccessControl.AccessControlType.Allow));
            info.SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite);
        }

        Assert.False(ExecutableSafety.IsSafe(file));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
