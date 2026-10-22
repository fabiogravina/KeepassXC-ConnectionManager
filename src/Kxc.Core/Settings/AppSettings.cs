using Kxc.Core.Entries;
using Kxc.Core.Search;

namespace Kxc.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Impostazioni salvate nella cartella di configurazione dell'utente: solo percorsi, preferenze,
/// preferiti e recenti (per UUID). Mai segreti.
/// </summary>
public sealed class AppSettings
{
    public const int MinAutoLockMinutes = 1;
    public const int MaxAutoLockMinutes = 60;
    public const int DefaultAutoLockMinutes = 5;
    public const int MaxRecents = 20;

    public string? DatabasePath { get; set; }

    public string? KeyFilePath { get; set; }

    public string? KeePassXcCliPath { get; set; }

    public int AutoLockMinutes { get; set; } = DefaultAutoLockMinutes;

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Codice lingua ("it", "en"); null segue la lingua del sistema.</summary>
    public string? Language { get; set; }

    public List<Guid> Favorites { get; set; } = [];

    public List<RecentConnection> Recents { get; set; } = [];

    public WindowPlacement? Window { get; set; }

    /// <summary>Client scelto per protocollo ("Rdp", "Ssh", "Sftp" → identificativo del client).</summary>
    public Dictionary<string, string> PreferredClients { get; set; } = new(StringComparer.Ordinal);

    public string? PreferredClient(ConnectionProtocols protocol) => PreferredClients.GetValueOrDefault(protocol.ToString());

    public void SetPreferredClient(ConnectionProtocols protocol, string clientId) => PreferredClients[protocol.ToString()] = clientId;

    public TimeSpan AutoLockTimeout =>
        TimeSpan.FromMinutes(Math.Clamp(AutoLockMinutes, MinAutoLockMinutes, MaxAutoLockMinutes));

    public bool ToggleFavorite(Guid uuid)
    {
        if (Favorites.Remove(uuid))
        {
            return false;
        }

        Favorites.Add(uuid);
        return true;
    }

    public void AddRecent(Guid uuid, DateTimeOffset connectedAt)
    {
        Recents.RemoveAll(r => r.Uuid == uuid);
        Recents.Insert(0, new RecentConnection(uuid, connectedAt));
        if (Recents.Count > MaxRecents)
        {
            Recents.RemoveRange(MaxRecents, Recents.Count - MaxRecents);
        }
    }
}

public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool Maximized);

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
