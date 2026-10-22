namespace Kxc.Core.Platform;

/// <summary>Eventi del sistema che bloccano l'app: blocco della sessione e sospensione (FR-03).</summary>
public interface ISystemSessionEvents : IDisposable
{
    event EventHandler<SystemLockReason>? LockRequired;

    /// <summary>False dove il sistema non permette di osservare questi eventi.</summary>
    bool IsSupported { get; }
}

public enum SystemLockReason
{
    SessionLocked,
    Suspending,
}
