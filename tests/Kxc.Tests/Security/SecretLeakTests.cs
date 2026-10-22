using System.Text;
using Kxc.Core.Entries;
using Kxc.Core.Platform;
using Kxc.Core.Settings;
using Kxc.Platform;
using Kxc.Platform.Executables;
using Kxc.Platform.Processes;
using Kxc.Platform.Settings;
using Kxc.Tests.TestData;
using Kxc.Vault;

namespace Kxc.Tests.Security;

/// <summary>
/// SEC-01 e SEC-08: dopo un ciclo completo (sblocco, lettura di una password, salvataggio delle
/// impostazioni) nessun segreto noto compare negli argomenti dei processi, nelle variabili
/// d'ambiente o nei file scritti dall'app.
/// </summary>
public sealed class SecretLeakTests : IDisposable
{
    private readonly string _configDirectory = Path.Combine(Path.GetTempPath(), "kxc-tests-" + Guid.NewGuid().ToString("N"));

    private sealed class RecordingRunner(IProcessRunner inner) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessOutcome> RunAsync(ProcessRequest request, ReadOnlyMemory<byte> standardInput, Func<Stream, CancellationToken, Task> readStandardOutput, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return inner.RunAsync(request, standardInput, readStandardOutput, cancellationToken);
        }
    }

    [Fact]
    public async Task Full_cycle_leaves_no_secret_in_arguments_environment_or_files()
    {
        var locator = new KeePassXcCliLocator(() => null);
        Assert.SkipWhen(locator.FindCli() is null, "keepassxc-cli non installato");

        var runner = new RecordingRunner(new ProcessRunner());
        using var vault = new KeePassXcVaultService(runner, locator);
        var snapshot = await vault.UnlockAsync(Fixture.Credentials(), TestContext.Current.CancellationToken);
        var entries = ConnectionCatalog.Build(snapshot.Entries, DateTimeOffset.UtcNow);
        foreach (var entry in entries.Where(e => e.CanConnect))
        {
            (await vault.ReadPasswordAsync(entry, TestContext.Current.CancellationToken)).Dispose();
        }

        var store = new JsonSettingsStore(new AppPaths(_configDirectory));
        var settings = new AppSettings { DatabasePath = Fixture.DatabasePath, KeyFilePath = Fixture.KeyFilePath };
        settings.ToggleFavorite(entries[0].Uuid);
        settings.AddRecent(entries[0].Uuid, DateTimeOffset.UtcNow);
        store.Save(settings);
        vault.Lock();

        var secrets = Fixture.EntrySecrets.Append(Fixture.DatabasePassword).ToList();
        var arguments = string.Join('\n', runner.Requests.SelectMany(r => r.Arguments));
        var environment = string.Join('\n', Environment.GetEnvironmentVariables().Values.Cast<object>());
        var files = string.Join('\n', Directory.EnumerateFiles(_configDirectory, "*", SearchOption.AllDirectories).Select(f => File.ReadAllText(f, Encoding.UTF8)));

        foreach (var secret in Fixture.EntrySecrets)
        {
            Assert.DoesNotContain(secret, arguments, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, environment, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, files, StringComparison.Ordinal);
        }

        // "test" è troppo comune per cercarlo nei percorsi: si verifica che nessun argomento sia la password.
        Assert.DoesNotContain(runner.Requests.SelectMany(r => r.Arguments), a => a == Fixture.DatabasePassword);
        Assert.True(secrets.Count > 1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_configDirectory))
        {
            Directory.Delete(_configDirectory, recursive: true);
        }
    }
}
