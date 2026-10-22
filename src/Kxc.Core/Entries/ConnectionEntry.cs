namespace Kxc.Core.Entries;

/// <summary>Voce connettibile mostrata nell'elenco. Non contiene segreti.</summary>
public sealed record ConnectionEntry
{
    /// <summary>Allegato in cui KeePassXC salva la configurazione dell'agente SSH della voce.</summary>
    public const string SshAgentSettingsAttachment = "KeeAgent.settings";

    public required Guid Uuid { get; init; }

    public required string Title { get; init; }

    public required string EntryPath { get; init; }

    public IReadOnlyList<string> GroupPath { get; init; } = [];

    public required ConnectionProtocols Protocols { get; init; }

    public string? Host { get; init; }

    /// <summary>Porta esplicita: campo Port, oppure porta di un URL rdp://, ssh:// o sftp://.</summary>
    public int? Port { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string? Domain { get; init; }

    public string Notes { get; init; } = string.Empty;

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// La voce è configurata per l'agente SSH di KeePassXC: SSH e SFTP usano la chiave
    /// dall'agente invece della password.
    /// </summary>
    public bool UsesSshAgent { get; init; }

    /// <summary>Problema che blocca tutti i protocolli della voce.</summary>
    public EntryWarning Warning { get; init; }

    /// <summary>Problema che blocca solo SSH e SFTP (per esempio il nome utente).</summary>
    public EntryWarning SshWarning { get; init; }

    public bool CanConnect => Warning == EntryWarning.None;

    public bool CanConnectWith(ConnectionProtocols protocol) =>
        CanConnect && (protocol == ConnectionProtocols.Rdp || SshWarning == EntryWarning.None);

    /// <summary>Avviso da mostrare: prima quello della voce, poi quello di SSH se la voce ha SSH o SFTP.</summary>
    public EntryWarning VisibleWarning =>
        Warning != EntryWarning.None ? Warning
        : (Protocols & (ConnectionProtocols.Ssh | ConnectionProtocols.Sftp)) != 0 ? SshWarning
        : EntryWarning.None;

    public ConnectionProtocols DefaultProtocol => ConnectionProtocolRules.Default(Protocols);

    public string GroupName => GroupPath.Count == 0 ? string.Empty : GroupPath[^1];

    public int PortFor(ConnectionProtocols protocol) => Port ?? ConnectionProtocolRules.DefaultPort(protocol);
}
