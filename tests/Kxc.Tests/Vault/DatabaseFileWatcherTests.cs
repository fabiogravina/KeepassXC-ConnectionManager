using Kxc.Vault;
using Microsoft.Extensions.Time.Testing;

namespace Kxc.Tests.Vault;

public sealed class DatabaseFileWatcherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kxc-watch-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;

    public DatabaseFileWatcherTests()
    {
        Directory.CreateDirectory(_directory);
        _file = Path.Combine(_directory, "db.kdbx");
        File.WriteAllText(_file, "versione 1");
    }

    [Fact]
    public async Task Change_is_reported_once_the_file_is_stable()
    {
        var time = new FakeTimeProvider();
        using var watcher = new DatabaseFileWatcher(time);
        var changes = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref changes);
        watcher.Watch(_file);

        await WriteAsync("versione 2, più lunga");
        await AdvanceAsync(time, DatabaseFileWatcher.PollInterval + DatabaseFileWatcher.StableFor * 3);

        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task File_still_being_written_is_not_reported()
    {
        var time = new FakeTimeProvider();
        using var watcher = new DatabaseFileWatcher(time);
        var changes = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref changes);
        watcher.Watch(_file);

        // La sincronizzazione scrive il file a pezzi: a ogni controllo la dimensione è cambiata.
        for (var i = 0; i < 6; i++)
        {
            await WriteAsync(new string('x', 100 * (i + 1)));
            await AdvanceAsync(time, DatabaseFileWatcher.StableFor);
        }

        Assert.Equal(0, changes);

        await AdvanceAsync(time, DatabaseFileWatcher.PollInterval + DatabaseFileWatcher.StableFor * 3);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Unchanged_file_and_stopped_watcher_report_nothing()
    {
        var time = new FakeTimeProvider();
        using var watcher = new DatabaseFileWatcher(time);
        var changes = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref changes);
        watcher.Watch(_file);

        await AdvanceAsync(time, DatabaseFileWatcher.PollInterval * 3);
        Assert.Equal(0, changes);

        watcher.StopWatching();
        await WriteAsync("versione 3, dopo lo stop");
        await AdvanceAsync(time, DatabaseFileWatcher.PollInterval * 3);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task Atomic_rename_like_keepassxc_is_reported()
    {
        var time = new FakeTimeProvider();
        using var watcher = new DatabaseFileWatcher(time);
        var changes = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref changes);
        watcher.Watch(_file);

        var temp = Path.Combine(_directory, "db.kdbx.tmp");
        await File.WriteAllTextAsync(temp, "salvato da KeePassXC", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(temp, DateTime.UtcNow.AddMinutes(1));
        File.Move(temp, _file, overwrite: true);
        await AdvanceAsync(time, DatabaseFileWatcher.PollInterval + DatabaseFileWatcher.StableFor * 3);

        Assert.Equal(1, changes);
    }

    private async Task WriteAsync(string content)
    {
        await File.WriteAllTextAsync(_file, content, TestContext.Current.CancellationToken);

        // Data di modifica distinta anche su filesystem con risoluzione grossolana.
        File.SetLastWriteTimeUtc(_file, File.GetLastWriteTimeUtc(_file).AddSeconds(1));
    }

    private static async Task AdvanceAsync(FakeTimeProvider time, TimeSpan total)
    {
        var step = TimeSpan.FromMilliseconds(500);
        for (var elapsed = TimeSpan.Zero; elapsed < total; elapsed += step)
        {
            time.Advance(step);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
