namespace Kxc.Core.Entries;

/// <summary>Protocolli disponibili per una voce, ricavati dai tag.</summary>
[Flags]
public enum ConnectionProtocols
{
    None = 0,
    Rdp = 1,
    Ssh = 2,
    Sftp = 4,
}

public static class ConnectionProtocolRules
{
    /// <summary>Ordine di priorità usato da Invio e doppio click: RDP, poi SSH, poi SFTP.</summary>
    public static IReadOnlyList<ConnectionProtocols> Priority { get; } =
        [ConnectionProtocols.Rdp, ConnectionProtocols.Ssh, ConnectionProtocols.Sftp];

    public static ConnectionProtocols Default(ConnectionProtocols available)
    {
        foreach (var protocol in Priority)
        {
            if (available.HasFlag(protocol))
            {
                return protocol;
            }
        }

        return ConnectionProtocols.None;
    }

    public static IEnumerable<ConnectionProtocols> Each(ConnectionProtocols available) =>
        Priority.Where(p => available.HasFlag(p));

    public static int DefaultPort(ConnectionProtocols protocol) => protocol switch
    {
        ConnectionProtocols.Rdp => 3389,
        ConnectionProtocols.Ssh or ConnectionProtocols.Sftp => 22,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Atteso un solo protocollo."),
    };

    /// <summary>Tag di protocollo, confrontati senza distinguere maiuscole e minuscole.</summary>
    public static ConnectionProtocols FromTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var result = ConnectionProtocols.None;
        foreach (var tag in tags)
        {
            if (tag.Equals("rdp", StringComparison.OrdinalIgnoreCase))
            {
                result |= ConnectionProtocols.Rdp;
            }
            else if (tag.Equals("ssh", StringComparison.OrdinalIgnoreCase))
            {
                result |= ConnectionProtocols.Ssh;
            }
            else if (tag.Equals("sftp", StringComparison.OrdinalIgnoreCase))
            {
                result |= ConnectionProtocols.Sftp;
            }
        }

        return result;
    }

    /// <summary>KeePassXC separa i tag con la virgola nell'export e accetta anche il punto e virgola.</summary>
    public static IReadOnlyList<string> SplitTags(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? []
            : tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
