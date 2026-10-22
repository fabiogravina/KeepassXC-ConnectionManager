using Kxc.Core.Locking;
using Microsoft.Extensions.Time.Testing;

namespace Kxc.Tests.Core;

public class AutoLockTimerTests
{
    [Fact]
    public void Locks_after_the_inactivity_timeout()
    {
        var time = new FakeTimeProvider();
        using var timer = new AutoLockTimer(time, TimeSpan.FromMinutes(5));
        var locks = 0;
        timer.LockRequired += (_, _) => locks++;

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.Equal(0, locks);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, locks);
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public void Activity_in_the_app_restarts_the_countdown()
    {
        var time = new FakeTimeProvider();
        using var timer = new AutoLockTimer(time, TimeSpan.FromMinutes(5));
        var locks = 0;
        timer.LockRequired += (_, _) => locks++;

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(4));
        timer.RegisterActivity();
        time.Advance(TimeSpan.FromMinutes(4));

        Assert.Equal(0, locks);
        Assert.Equal(TimeSpan.FromMinutes(1), timer.Remaining);
    }

    [Fact]
    public void Lock_fires_once_and_stop_prevents_it()
    {
        var time = new FakeTimeProvider();
        using var timer = new AutoLockTimer(time, TimeSpan.FromMinutes(1));
        var locks = 0;
        timer.LockRequired += (_, _) => locks++;

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, locks);

        timer.Start();
        timer.Stop();
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, locks);
    }

    [Fact]
    public void Tick_reports_the_remaining_time()
    {
        var time = new FakeTimeProvider();
        using var timer = new AutoLockTimer(time, TimeSpan.FromMinutes(5));
        TimeSpan? last = null;
        timer.Tick += (_, remaining) => last = remaining;

        timer.Start();
        time.Advance(TimeSpan.FromSeconds(60));

        Assert.Equal(TimeSpan.FromMinutes(4), last);
    }
}
