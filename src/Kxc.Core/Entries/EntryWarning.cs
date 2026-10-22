namespace Kxc.Core.Entries;

/// <summary>Problemi che non nascondono la voce ma ne impediscono la connessione.</summary>
public enum EntryWarning
{
    None,

    /// <summary>Né il campo Host né l'URL contengono un host.</summary>
    MissingHost,

    /// <summary>Host, porta, utente o dominio non superano la validazione.</summary>
    InvalidConnectionData,

    /// <summary>Un'altra voce ha lo stesso percorso: keepassxc-cli non saprebbe quale leggere.</summary>
    AmbiguousPath,
}
