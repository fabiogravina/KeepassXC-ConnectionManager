using System.Runtime.Versioning;
using Kxc.Core.Platform;
using Microsoft.Win32;

namespace Kxc.Platform.Session;

public static class SystemSessionEvents
{
    public static ISystemSessionEvents Create() =>
        OperatingSystem.IsWindows() ? new WindowsSessionEvents() : new UnsupportedSessionEvents();
}

/// <summary>Blocco della sessione e sospensione su Windows.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSessionEvents : ISystemSessionEvents
{
    public WindowsSessionEvents()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event EventHandler<SystemLockReason>? LockRequired;

    public bool IsSupported => true;

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff
            or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            LockRequired?.Invoke(this, SystemLockReason.SessionLocked);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            LockRequired?.Invoke(this, SystemLockReason.Suspending);
        }
    }
}

/// <summary>
/// Linux: il blocco dello schermo e la sospensione arrivano da D-Bus (logind e
/// org.freedesktop.ScreenSaver) e saranno collegati dopo la verifica su Kubuntu.
/// </summary>
public sealed class UnsupportedSessionEvents : ISystemSessionEvents
{
    public event EventHandler<SystemLockReason>? LockRequired
    {
        add { }
        remove { }
    }

    public bool IsSupported => false;

    public void Dispose()
    {
    }
}
