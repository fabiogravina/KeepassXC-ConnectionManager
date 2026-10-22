# KXC-ConnectionManager

App desktop per Windows 11 e Linux che legge un database KeePassXC esistente e avvia con un click
sessioni RDP, SSH e SFTP, senza duplicare le credenziali. Licenza GNU AGPL v3.

Specifiche: [Docs/Specifiche - KXC-ConnectionManager.md](Docs/Specifiche%20-%20KXC-ConnectionManager.md).

## Requisiti

- .NET SDK 10
- KeePassXC 2.7.0 o successivo, con `keepassxc-cli`

## Comandi

```sh
dotnet build Kxc.sln
dotnet test --solution Kxc.sln
dotnet run --project src/Kxc.App
```

I test di integrazione usano il `keepassxc-cli` installato e vengono saltati se manca. I test
dell'interfaccia salvano le schermate in `tests/Kxc.Tests/bin/<config>/net10.0/screenshots`.

## Pacchetti

```powershell
pwsh build/publish.ps1 -Runtime win-x64     # publish/KXC-ConnectionManager-<versione>-win-x64.zip
pwsh build/publish.ps1 -Runtime linux-x64   # publish/KXC-ConnectionManager-<versione>-linux-x64.tar.gz
```

I pacchetti sono autonomi: non serve installare .NET. Serve KeePassXC 2.7.0 o successivo.

**Firma e verifica.** Con un certificato di firma del codice lo script firma con Authenticode
l'app, le sue DLL e `kxc-askpass` (Smart App Control di Windows 11 può bloccare i binari non
firmati); per Linux `-GpgKey` produce una firma `.asc`. Ogni pubblicazione aggiorna
`publish/SHA256SUMS`.

```powershell
pwsh build/publish.ps1 -Runtime win-x64 -CertificateThumbprint <impronta> -RequireSignature
pwsh build/publish.ps1 -Runtime linux-x64 -GpgKey <id chiave>
```

Per verificare un pacchetto scaricato: `Get-FileHash -Algorithm SHA256 <archivio>` (o `sha256sum`)
e confronto con `SHA256SUMS`; su Windows, `Get-AuthenticodeSignature KXC-ConnectionManager.exe`.

- **Windows:** estrai lo zip in una cartella e avvia `KXC-ConnectionManager.exe`.
- **Linux:** estrai in `~/.local/opt/kxc-connectionmanager`, rendi eseguibili i due programmi
  (`chmod +x KXC-ConnectionManager kxc-askpass`: un archivio creato su Windows non conserva il
  permesso), copia `kxc-connectionmanager.desktop` in `~/.local/share/applications` e la cartella
  `icons/hicolor` in `~/.local/share/icons/hicolor`.

L'icona si rigenera da un'unica definizione con `dotnet run build/icons/icon.cs`; le proposte
scartate sono in `Docs/icons/proposte`.

## Struttura

| Progetto | Responsabilità |
| --- | --- |
| `src/Kxc.Core` | Modelli, regole sulle voci, ricerca, blocco automatico, interfacce |
| `src/Kxc.Vault` | Lettura del database con `keepassxc-cli` (solo comandi di lettura) |
| `src/Kxc.Connectors` | Connettori RDP, SSH e SFTP (mstsc, OpenSSH in Windows Terminal, WinSCP) |
| `src/Kxc.AskPass` | Helper `kxc-askpass` per `SSH_ASKPASS`: chiede la risposta all'app su un canale locale |
| `src/Kxc.Platform` | Processi, percorsi di configurazione, impostazioni, Gestore credenziali, eventi di sistema |
| `src/Kxc.App` | Interfaccia Avalonia (MVVM) e composizione delle dipendenze |
| `tests/Kxc.Tests` | Test unitari, di integrazione, di sicurezza e dell'interfaccia |
| `tests/Fixtures` | Database di prova con dati inventati |
