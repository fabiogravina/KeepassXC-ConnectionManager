namespace Kxc.Core.Entries;

/// <summary>Problemi che non nascondono la voce ma ne impediscono la connessione.</summary>
public enum EntryWarning
{
    None,

    /// <summary>Né il campo Host né l'URL contengono un host.</summary>
    MissingHost,

    /// <summary>Host, porta, utente o dominio non superano la validazione.</summary>
    InvalidConnectionData,

    /// <summary>
    /// Un'altra voce, anche scaduta, ha lo stesso percorso: keepassxc-cli potrebbe restituirne la
    /// password (S2 dell'analisi di sicurezza).
    /// </summary>
    AmbiguousPath,

    /// <summary>Il nome utente contiene caratteri non ammessi per SSH e SFTP; RDP resta possibile.</summary>
    UnsafeSshUserName,
}
