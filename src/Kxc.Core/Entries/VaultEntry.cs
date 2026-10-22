namespace Kxc.Core.Entries;

/// <summary>
/// Voce così come letta dal database, senza alcun valore protetto: password e campi
/// protetti non vengono mai copiati qui.
/// </summary>
public sealed record VaultEntry
{
    public required Guid Uuid { get; init; }

    public required string Title { get; init; }

    public string UserName { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public string Tags { get; init; } = string.Empty;

    /// <summary>Cartelle dalla radice (esclusa) fino al gruppo della voce.</summary>
    public IReadOnlyList<string> GroupPath { get; init; } = [];

    /// <summary>Campi personalizzati non protetti (per esempio Host, Port, Domain).</summary>
    public IReadOnlyDictionary<string, string> CustomFields { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<string> AttachmentNames { get; init; } = [];

    /// <summary>Scadenza, solo se la voce ha la scadenza attiva.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Percorso usato da keepassxc-cli per leggere la voce: gruppi e titolo separati da '/'.</summary>
    public string EntryPath => GroupPath.Count == 0 ? Title : string.Join('/', GroupPath) + "/" + Title;
}
