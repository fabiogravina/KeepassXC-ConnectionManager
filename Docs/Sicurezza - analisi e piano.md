# Sicurezza: analisi del codice e piano di correzione

22 ottobre 2026 · revisione completa del codice al commit `a980b6f` · nessuna modifica al codice

## Sintesi

La base è solida: nessuna shell, argomenti sempre in lista, password del database solo su stdin, segreti delle voci passati ai client via `CredWrite` o named pipe riservati all'utente, XML letto senza DTD, nessuna connessione di rete propria, pacchetti bloccati e senza vulnerabilità note (`dotnet list package --vulnerable`: nessuna).

L'analisi ha però trovato **due vulnerabilità alte, confermate con prove pratiche**, che vanno corrette prima di usare l'app con database che contengono voci non scritte da te (importazioni da elenchi di clienti, database condivisi):

1. **Iniezione di opzioni in ssh/sftp tramite Windows Terminal** a partire dal nome utente di una voce: può portare all'esecuzione di un comando.
2. **Password della voce sbagliata** quando una voce scaduta ha lo stesso percorso di una valida: la password di un host viene inviata a un altro.

Seguono quattro problemi di gravità media e una serie di rafforzamenti.

## Modello di minaccia usato

| Considerato | Escluso (come da specifica) |
| --- | --- |
| **Contenuto del database non fidato**: titoli, utenti, host, URL, tag, note possono arrivare da importazioni o da altri che usano lo stesso database. Non devono poter eseguire comandi né dirottare password. | Malware che gira con i privilegi dell'utente, lettura della memoria del processo, debugger, keylogger. |
| **Altri utenti dello stesso computer**: non devono leggere segreti né sostituire eseguibili. | Amministratori della macchina. |
| **Server remoti e configurazione SSH dell'utente**: una password deve arrivare solo all'host della voce. | Compromissione di KeePassXC stesso. |
| **Persistenza involontaria**: segreti su disco, nei log, in credenziali residue. | |

## Riepilogo dei risultati

| ID | Gravità | Stato | Problema |
| --- | --- | --- | --- |
| S1 | **Alta** | **Corretto** (22/10) | Iniezione di opzioni in ssh/sftp tramite `wt.exe` dal nome utente della voce |
| S2 | **Alta** | **Corretto** (22/10) | Con una voce scaduta omonima viene letta e inviata la password della voce sbagliata |
| S3 | Media | **Corretto** (22/10) | La password SSH va alla prima domanda «password», anche se la fa un jump host |
| S4 | Media | **Corretto** (22/10) | Blocco e chiusura dell'app si piantano se è aperta la finestra della host key |
| S5 | Media | **Corretto** (22/10) | Pacchetto portabile in una cartella scrivibile da altri utenti: sostituzione di DLL o di `kxc-askpass` |
| S6 | Media | **Corretto** (22/10) | A ogni sblocco tutte le password del database transitano in chiaro nei buffer del processo |
| S7 | Media-bassa | **Corretto** (22/10) | RDP: una credenziale già salvata dall'utente per lo stesso host viene sovrascritta e cancellata |
| S8 | Bassa | **Corretto** (22/10, Linux in parte) | Controllo degli eseguibili incompleto e non applicato a WinSCP, `wt.exe`, `kxc-askpass` |
| S9 | Bassa | **Corretto** (22/10) | Percorsi di voce che iniziano con `-` interpretati come opzioni da keepassxc-cli |
| S10 | Bassa | **Corretto** (22/10) | Una connessione avviata appena prima del blocco prosegue dopo il blocco |
| S11 | Bassa | **Confermato e corretto** (22/10) | Campo password: copie immutabili, cronologia di annullamento, copia con «Mostra» |
| S12 | Bassa | **Corretto** (22/10, dump WER no) | Log di Avalonia su Trace anche in Release; dump in caso di crash |
| S13 | Bassa | **Corretto** (22/10, avviso) | Database o key file su percorso UNC: autenticazione NTLM verso un server remoto |
| S14 | Bassa | **Corretto** (22/10) | XML con gruppi annidati molto in profondità: stack overflow (crash) |
| S15 | Bassa | **Corretto** (23/10, da provare su Kubuntu) | Linux: nessun blocco con lo schermo e alla sospensione |
| S16 | **Media** | **In parte corretto** (22/10): manca il certificato | Catena di fornitura: Actions non fissate, KeePassXC in CI senza versione, pacchetti non firmati (Smart App Control blocca a volte `kxc-askpass` non firmato) |
| S17 | Info | **Verificato: nessun problema** (23/10) | Percorso di `SSH_ASKPASS` con spazi e modo in cui OpenSSH per Windows lo avvia |

---

## S1 · Iniezione di opzioni in ssh/sftp tramite Windows Terminal — Alta, confermato

**Dove:** [WindowsOpenSshConnector.cs:140-170](../src/Kxc.Connectors/Ssh/WindowsOpenSshConnector.cs#L140-L170), [ConnectionInputValidator.cs:35-41](../src/Kxc.Core/Entries/ConnectionInputValidator.cs#L35-L41).

**Problema.** Gli argomenti di ssh sono passati a `wt.exe`, che ricostruisce la riga di comando della nuova scheda senza fare l'escape delle virgolette. Il nome utente è validato solo contro il `-` iniziale e i caratteri di controllo, quindi può contenere virgolette e spazi.

**Prova (22 ottobre 2026).** Una sonda che registra gli argomenti, avviata da `wt.exe` come fa l'app, con nome utente `x" -oProxyCommand=calc "`:

```text
inviato:   -o  NumberOfPasswordPrompts=1  -o  User=x" -oProxyCommand=calc "  --  192.0.2.1
ricevuto:  -o  NumberOfPasswordPrompts=1  -o  User=x  -oProxyCommand=calc  ""  --  192.0.2.1
```

ssh riceve un `-oProxyCommand` aggiuntivo: alla pressione di «SSH» o «SFTP» su quella voce verrebbe eseguito un comando arbitrario. La modalità console (senza `wt.exe`) e WinSCP non sono colpite: lì gli argomenti arrivano intatti. Il titolo con le virgolette, nella stessa prova, non ha alterato gli argomenti.

**Correzione.**
1. Per SSH e SFTP accettare solo nomi utente con caratteri sicuri, per esempio `^[A-Za-z0-9._@\\-]{1,64}$` e niente `-` iniziale; gli altri diventano un avviso sulla voce («nome utente non utilizzabile con SSH»). È la difesa principale, perché non dipende da come un programma terzo reinterpreta la riga di comando.
2. Ripulire il titolo passato a `--title` (togliere virgolette, `;`, caratteri di controllo) o costruirlo solo dall'host validato.
3. Lo stesso controllo copre anche CVE-2023-51385 (metacaratteri di shell nel nome utente usati da un `ProxyCommand` con `%r` nella configurazione ssh dell'utente) sulle versioni di OpenSSH non aggiornate.

**Test da aggiungere.** Unitario: il catalogo segnala come non connettibile per SSH una voce con utente `x" -oProxyCommand=calc "`. Integrazione su Windows: la sonda di argomenti tramite `wt.exe` (con `dotnet.exe` + DLL, perché Smart App Control blocca gli eseguibili non firmati) riceve esattamente gli argomenti attesi.

## S2 · Password della voce sbagliata con voci scadute omonime — Alta, confermato

**Dove:** [ConnectionCatalog.cs:18-32](../src/Kxc.Core/Entries/ConnectionCatalog.cs#L18-L32), [KeePassXcVaultService.cs:81-108](../src/Kxc.Vault/KeePassXcVaultService.cs#L81-L108).

**Problema.** La password si legge con `keepassxc-cli show <percorso>`, che restituisce la prima voce con quel percorso, anche se scaduta. Il controllo delle ambiguità conta solo le voci non scadute, quindi una voce valida con lo stesso percorso di una scaduta non viene segnalata.

**Prova (22 ottobre 2026).** Nella cartella «Clienti» una voce «Server» scaduta (host `vecchio.example`) e una valida (host `nuovo.example`). L'elenco mostra solo quella valida; `show Clienti/Server` restituisce `password-della-voce-SCADUTA` e l'UUID della voce scaduta. L'app collegherebbe `nuovo.example` con la password di `vecchio.example`. È uno scenario comune: quando si cambia una password, si lascia scadere la voce vecchia e se ne crea una nuova con lo stesso nome.

**Correzione.**
1. Leggere la password con `show -s -a Uuid -a Password` e **confrontare l'UUID** con quello della voce scelta; se non coincide, rifiutare la connessione. La prova ha confermato che `show` restituisce gli attributi nell'ordine richiesto. Attenzione: un attributo vuoto non stampa nessuna riga (fase 0), quindi l'UUID va chiesto per primo e una password vuota è l'assenza della seconda riga.
2. Contare le ambiguità su **tutte** le voci, scadute comprese.

**Test da aggiungere.** Fixture con una voce scaduta e una valida omonime: la voce risulta ambigua; con un `show` simulato che restituisce l'UUID sbagliato la connessione viene rifiutata.

## S3 · Password SSH consegnata senza verificare chi la chiede — Media

**Dove:** [AskPassServer.cs:101-106](../src/Kxc.Connectors/Ssh/AskPassServer.cs#L101-L106), [AskPassPrompt.cs:24-40](../src/Kxc.Connectors/Ssh/AskPassPrompt.cs#L24-L40).

**Problema.** Il canale consegna la password alla prima domanda che contiene «password». Se la configurazione ssh dell'utente (`~/.ssh/config`) prevede un `ProxyJump` o un `ProxyCommand` con ssh per quell'host, la prima domanda arriva dal **jump host** («utente@jump's password:»), che riceverebbe la password della voce.

**Correzione.** Accettare solo le domande che contengono `utente@host` della voce, nelle due forme di OpenSSH: `utente@host's password:` e, per keyboard-interactive (OpenSSH 8.4 e successivi), `(utente@host) Password:`. Le altre vengono rifiutate. In aggiunta, valutare `-o ForwardAgent=no` di default, così una voce non concede l'agente al server remoto senza volerlo.

**Test da aggiungere.** Classificazione: `pi@192.168.178.210's password:` accettata per la voce `pi`@`192.168.178.210`; `pi@jump.example's password:` rifiutata.

## S4 · Blocco e chiusura si piantano con la finestra della host key aperta — Media

**Dove:** [WindowsOpenSshConnector.cs:121-134](../src/Kxc.Connectors/Ssh/WindowsOpenSshConnector.cs#L121-L134), [HostKeyConfirmationService.cs:17-29](../src/Kxc.App/Services/HostKeyConfirmationService.cs#L17-L29), [ShellViewModel.cs:95-109](../src/Kxc.App/ViewModels/ShellViewModel.cs#L95-L109).

**Problema.** `CleanUp` attende in modo sincrono (`GetAwaiter().GetResult()`) sul thread dell'interfaccia la chiusura del canale. Se il canale sta aspettando la risposta della finestra della host key, quella risposta ha bisogno proprio del thread dell'interfaccia: è un deadlock. Succede quando scatta il blocco automatico con la finestra aperta, cioè quando l'utente si è allontanato, e anche alla chiusura dell'app. Il database viene azzerato prima del blocco dell'interfaccia, ma la password nel canale resta in memoria e l'app va terminata a mano. Lo stesso schema bloccante è in `WinScpConnector.CleanUp`.

**Correzione.** Rendere la pulizia non bloccante: annullare subito i canali (in modo sincrono) e lasciare che si chiudano in background, oppure `CleanUpAsync` attesa senza bloccare il thread dell'interfaccia. Chiudere la finestra della host key con risposta «no» prima di attendere il canale.

**Test da aggiungere.** Interfaccia headless: finestra della host key aperta, poi Ctrl+L; il blocco termina entro un secondo e la finestra si chiude.

## S5 · Pacchetto portabile in una cartella scrivibile da altri utenti — Media

**Dove:** distribuzione ([build/publish.ps1](../build/publish.ps1)), [AppServices.cs:75](../src/Kxc.App/Services/AppServices.cs#L75).

**Problema.** Lo zip va estratto in una cartella a scelta. Sotto `C:\` le sottocartelle create da un utente ereditano di norma «Modifica» per *Authenticated Users*: un altro utente del PC potrebbe sostituire una DLL dell'app o `kxc-askpass.exe` e ricevere la master password o le password delle voci. Il controllo degli eseguibili oggi vale solo per keepassxc-cli.

**Correzione.** All'avvio, verificare con lo stesso controllo degli eseguibili la cartella dell'app e `kxc-askpass`, e mostrare un avviso chiaro se sono modificabili da altri. Documentare l'installazione in `%LOCALAPPDATA%\Programs` o in `Program Files`, e in seguito fornire un installer.

## S6 · Tutte le password del database transitano nei buffer del processo — Media

**Dove:** [KeePassXcVaultService.cs:208-239](../src/Kxc.Vault/KeePassXcVaultService.cs#L208-L239), [KeePassXcExportParser.cs:21-59](../src/Kxc.Vault/KeePassXcExportParser.cs#L21-L59).

**Problema.** `export -f xml` scrive su stdout l'intero database decifrato, con tutte le password e gli allegati (comprese eventuali chiavi SSH). Il parser non li trasforma mai in stringhe, ma i byte passano dai buffer della pipe e dal buffer interno di `XmlReader`, che non vengono azzerati: dopo ogni sblocco e ogni ricarica restano in memoria fino al riuso, e possono finire nel file di paging, nell'ibernazione o in un dump. La specifica esclude la lettura della memoria, ma SEC-02 è rispettato solo in parte.

**Correzione.** Inserire tra stdout e `XmlReader` un flusso filtrante, con buffer propri bloccati e azzerati, che sostituisce con spazi il contenuto di ogni `<Value ProtectInMemory="True">` e di `<Binaries>` prima che il parser lo veda. In alternativa, più lenta: `ls` + `show` senza `-s` per voce. Aggiornare la specifica sul limite residuo (il processo keepassxc-cli e il buffer del kernel).

**Test da aggiungere.** Il filtro, su una fixture, produce un XML in cui nessun segreto noto compare e che il parser legge come prima.

## S7 · RDP: credenziale dell'utente sovrascritta e poi cancellata — Media-bassa

**Dove:** [WindowsCredentialStore.cs:19-56](../src/Kxc.Platform/Credentials/WindowsCredentialStore.cs#L19-L56), [WindowsRdpConnector.cs:129-150](../src/Kxc.Connectors/Rdp/WindowsRdpConnector.cs#L129-L150).

**Problema.** Se per `TERMSRV/<host>` esiste già una credenziale salvata dall'utente in mstsc («Memorizza credenziali»), `CredWrite` la sostituisce e la pulizia la cancella: la credenziale dell'utente va persa. Se l'app viene terminata di colpo, la credenziale temporanea resta nel Gestore credenziali fino al logoff (è di sessione: non è scritta su disco).

**Correzione.** `CredRead` prima di scrivere: se esiste già, non sovrascrivere e dirlo all'utente. Marcare le credenziali dell'app con un commento (per esempio «KXC-ConnectionManager») e, all'avvio, eliminare quelle rimaste da una sessione precedente.

## S8 · Controllo degli eseguibili incompleto — Bassa

**Dove:** [ExecutableSafety.cs:13-73](../src/Kxc.Platform/Executables/ExecutableSafety.cs#L13-L73).

**Problemi.**
- **Windows:** sono considerati solo Everyone, Users e Authenticated Users. Restano fuori INTERACTIVE, Guests, gruppi di dominio come *Domain Users* (rilevante su un PC in dominio) e singoli utenti. Il proprietario del file non viene controllato (il proprietario può sempre cambiare i permessi), e manca `DeleteSubdirectoriesAndFiles` sulla cartella.
- **Linux:** non vengono controllati il proprietario (deve essere root o l'utente corrente) né la destinazione dei collegamenti simbolici e la sua cartella.
- Le cartelle superiori non sono controllate.
- Il controllo non si applica a WinSCP, `wt.exe` e `kxc-askpass`.

**Correzione.** Passare a una lista di principali ammessi (SYSTEM, Administrators, TrustedInstaller, utente corrente) per qualsiasi diritto di scrittura, controllare il proprietario, risolvere i collegamenti e risalire la catena delle cartelle. Applicare il controllo a tutti i client avviati.

## S9 · Percorsi di voce che iniziano con `-` — Bassa, confermato

**Dove:** [KeePassXcCommands.cs:14-18](../src/Kxc.Vault/KeePassXcCommands.cs#L14-L18).

**Prova.** Una voce in radice con titolo `--all` fa fallire `show` («argomenti posizionali mancanti»); con `--` prima del percorso la voce viene letta correttamente. Nessuna fuga di dati verificata, ma il comportamento dipende dalle opzioni di keepassxc-cli.

**Correzione.** Inserire `--` prima degli argomenti posizionali (database e voce) in `export` e `show`; verificarlo anche con keepassxc-cli 2.7.0.

## S10 · Connessione che prosegue dopo il blocco — Bassa

**Dove:** [MainViewModel.cs:206-235](../src/Kxc.App/ViewModels/MainViewModel.cs#L206-L235) e connettori.

**Problema.** Se si blocca l'app nei circa 0,8 secondi in cui la password viene letta, la lettura finisce lo stesso: il canale di askpass o la credenziale RDP vengono creati dopo la pulizia del blocco. Il canale resta attivo fino a 2 minuti.

**Correzione.** Passare a `LaunchAsync` un token annullato dal blocco e controllarlo subito dopo la lettura della password, azzerandola e annullando l'avvio.

## S11 · Campo della master password — Bassa, da verificare

**Dove:** [UnlockView.axaml](../src/Kxc.App/Views/UnlockView.axaml), [UnlockViewModel.cs:80-119](../src/Kxc.App/ViewModels/UnlockViewModel.cs#L80-L119).

Limite già noto di SEC-02: ogni carattere digitato crea una nuova stringa immutabile, che non si può azzerare. Da verificare in Avalonia 12:
- se la cronologia di annullamento del `TextBox` conserva copie della password;
- se con «Mostra» attivo si può copiare la password negli appunti (la cronologia degli appunti di Windows può sincronizzarla nel cloud);
- se il valore è esposto all'automazione dell'interfaccia.

**Correzione.** `IsUndoEnabled="False"`, nessuna copia o taglio dal campo password, nemmeno con «Mostra», e verifica del peer di automazione. Sul lungo periodo, un controllo dedicato che tenga i caratteri in un buffer azzerabile.

## S12 · Log e dump — Bassa

**Dove:** [Program.cs:16](../src/Kxc.App/Program.cs#L16).

`LogToTrace()` è attivo anche in Release: Avalonia scrive avvisi su Trace (visibili con un debugger o DebugView), e gli errori di binding possono includere valori. Un crash può produrre un dump con i segreti in memoria, se Windows Error Reporting o le variabili `DOTNET_DbgEnableMiniDump` lo prevedono. Inoltre i comandi asincroni intercettano solo `ConnectionException`: qualsiasi altra eccezione chiude l'app.

**Correzione.** Log solo in Debug, o al livello Error senza valori. Gestore globale delle eccezioni che blocca e azzera prima di uscire. Valutare l'esclusione del processo dai dump di WER.

## S13 · Percorsi UNC — Bassa

Un database o un key file su `\\server\condivisione` fa sì che Windows si autentichi con NTLM verso quel server. Serve che sia l'utente a scegliere quel percorso. **Correzione:** avviso o rifiuto dei percorsi UNC non attendibili.

## S14 · Annidamento XML molto profondo — Bassa

**Dove:** [KeePassXcExportParser.cs:100-129](../src/Kxc.Vault/KeePassXcExportParser.cs#L100-L129). La lettura dei gruppi è ricorsiva: un database con migliaia di livelli di gruppi causa uno stack overflow, che chiude l'app senza possibilità di intercettarlo. **Correzione:** limite di profondità (per esempio 200) con errore «database non leggibile».

## S15 · Linux senza blocco con lo schermo — Bassa, noto

[SystemSessionEvents.cs](../src/Kxc.Platform/Session/SystemSessionEvents.cs): su Linux il blocco con lo schermo e alla sospensione (FR-03, SEC-03) non è ancora implementato. Va fatto prima di distribuire la versione Linux (D-Bus: logind `PrepareForSleep`, `org.freedesktop.ScreenSaver.ActiveChanged`).

## S16 · Catena di fornitura — Bassa

- [ci.yml:18-31](../.github/workflows/ci.yml#L18-L31): `actions/checkout@v4` e `actions/setup-dotnet@v4` sono riferiti per etichetta, non per hash del commit; `choco install keepassxc` senza versione.
- I pacchetti pubblicati non sono firmati (Authenticode) e non hanno checksum.
- [build/icons/icon.cs](../build/icons/icon.cs) scarica SkiaSharp fuori dal lock file (solo strumento di sviluppo).

**Correzione.** Fissare le Actions all'hash, fissare la versione di KeePassXC in CI, firmare gli eseguibili, pubblicare `SHA256SUMS` e, se utile, una SBOM.

## S17 · Avvio di `SSH_ASKPASS` con spazi nel percorso — Info, da verificare

Se l'app sta in una cartella con spazi (per esempio `C:\Program Files\...`), va verificato che OpenSSH per Windows avvii `kxc-askpass.exe` con il percorso tra virgolette, e non con il classico problema del percorso senza virgolette (`C:\Program.exe`). L'impatto è basso, perché scrivere in `C:\` richiede i diritti di amministratore, ma la prova è semplice.

---

## Cosa è già fatto bene

- Nessuna shell: tutti i processi partono con argomenti in lista e percorsi assoluti.
- Password del database solo su stdin, in buffer bloccati e azzerati; password delle voci lette solo alla connessione.
- RDP con `CredWrite` di sessione, al posto di `cmdkey` con la password negli argomenti.
- Canali verso ssh e WinSCP: named pipe con nome casuale a 128 bit, una sola istanza, accessibili solo all'utente corrente e verificati anche dal lato client.
- Domande di ssh diverse da password e host key sempre rifiutate; una sola richiesta di password (`NumberOfPasswordPrompts=1`); host key confermata dall'utente con «Annulla» predefinito.
- XML letto con DTD vietati e senza resolver (niente XXE); valori protetti e allegati saltati.
- Host validati e preceduti da `--`; utente codificato nell'URL di WinSCP.
- Nessuna connessione di rete propria (nemmeno DNS); impostazioni senza segreti.
- Pacchetti NuGet bloccati, audit attivo, nessuna vulnerabilità nota oggi.
- `TestData/` escluso dal repository e fixture rigenerate con dati inventati.

## Stato delle correzioni

**Fase 1 completata il 22 ottobre 2026** (206 test: 204 verdi, 2 saltati perché manca la fixture KDBX 4; prova SSH e SFTP reale sul Raspberry riuscita).

- **S1.** Nuova regola `ConnectionInputValidator.IsValidSshUserName`: per SSH e SFTP solo lettere, cifre e `. _ @ \ + -`, senza `-` iniziale. Una voce con un nome utente diverso mostra l'avviso «nome utente non ammesso per SSH e SFTP» e ha i pulsanti SSH e SFTP disattivati; RDP resta utilizzabile (`ConnectionEntry.SshWarning`, `CanConnectWith`). Difese indipendenti nel connettore: nome utente e host ricontrollati prima di leggere la password, qualsiasi argomento con virgolette rifiutato prima di `wt.exe`, titolo della scheda ripulito da virgolette, `\`, `;`, `` ` `` e caratteri di controllo.
- **S2.** La password si legge con `show -s -a Uuid -a Password`: se l'UUID restituito non è quello della voce scelta, la lettura viene rifiutata (`VaultError.EntryMismatch`) e la password azzerata. Le ambiguità di percorso si contano su tutte le voci, scadute comprese.
- **S9.** `--` prima degli argomenti posizionali in `export` e `show`.
- **Test di regressione.** `tests/Fixtures/fixture-casi.kdbx` (dati inventati) riproduce le prove: voce scaduta omonima e titolo `--all`; `SecurityRegressionTests` le esegue con keepassxc-cli vero. Nuovi test unitari per il nome utente SSH, l'argomento con virgolette verso `wt.exe`, la verifica dell'UUID, la password vuota e su più righe.

**Fase 2 completata il 22 ottobre 2026** (222 test: 220 verdi, 2 saltati per la fixture KDBX 4; prova SSH e SFTP reale sul Raspberry riuscita).

- **S3.** Il canale consegna la password solo a una domanda che nomina `utente@host` della voce (`pi@host's password:` o `(pi@host) Password:`); la domanda di un jump host viene rifiutata. ssh parte con `-o ForwardAgent=no`.
- **S4.** `CleanUp` non attende più: annulla i canali e azzera subito le password (`AskPassServer.Cancel`, `OneShotSecretPipe.Cancel`); la finestra della host key si chiude con risposta «no». Test con una conferma che non risponde mai e un vero `kxc-askpass` in attesa.
- **S5.** All'avvio si controllano eseguibile, cartella dell'app e `kxc-askpass` con lo stesso controllo usato per keepassxc-cli; se sono modificabili da altri utenti, la schermata di sblocco mostra un avviso prima della password (`InstallationStatus`). Nota: con la cartella del progetto sotto `D:\` (permessi ereditati da *Authenticated Users*) l'avviso compare anche nelle build di sviluppo, correttamente.
- **S7.** Le credenziali dell'app portano il commento «KXC-ConnectionManager». Una credenziale dell'utente per lo stesso host non viene né sovrascritta né cancellata (mstsc usa quella); i residui dell'app vengono sostituiti ed eliminati all'avvio (`CredEnumerate` su `TERMSRV/*`). Test anche sul Gestore credenziali vero, con destinazioni `.invalid`.
- **S10.** Allo sblocco nasce un token di sessione annullato dal blocco; i connettori lo controllano subito dopo la lettura della password e, se l'app si è bloccata, la azzerano e non avviano il client né scrivono credenziali.

**Nuovo elemento emerso durante i test (S16, ora gravità media).** Sul PC di sviluppo è attivo **Smart App Control** di Windows 11. Almeno due volte ha bloccato la prima esecuzione di un `kxc-askpass.dll` appena compilato («Un criterio di controllo dell'applicazione ha bloccato il file»); pochi minuti dopo lo stesso file veniva eseguito. Su un PC con Smart App Control attivo un `kxc-askpass` non firmato può quindi essere bloccato: in quel caso ssh non riceve la password e la connessione fallisce (senza fughe di dati). La firma Authenticode di app e helper diventa necessaria per la distribuzione su Windows 11.

**Fase 3 completata il 22 ottobre 2026** (241 test: 239 verdi, 2 saltati per la fixture KDBX 4; sblocco del database KDBX 4 reale e prova SSH/SFTP sul Raspberry riusciti).

- **S6.** `ProtectedValueFilterStream` sta tra stdout di keepassxc-cli e `XmlReader`: sostituisce con spazi, nello stesso buffer in cui arrivano, il testo dei valori `ProtectInMemory="True"`, di ogni valore della chiave `Password` (anche se il database non la protegge in memoria) e di tutto `<Binaries>`. Il parser non vede mai segreti né allegati. Test anche leggendo un byte alla volta. *Resta:* i buffer della pipe del sistema operativo e la memoria del processo keepassxc-cli, fuori dal controllo dell'app.
- **S8.** Il controllo degli eseguibili usa una lista di principali ammessi (utente corrente, SYSTEM, Administrators, TrustedInstaller) per ogni diritto di scrittura, verifica il proprietario e risale le cartelle superiori (chi può rinominarle o prenderne il controllo rende l'eseguibile non sicuro). Su Linux segue i collegamenti simbolici e risale le cartelle, ignorando quelle con sticky bit. Il controllo vale ora anche per mstsc, ssh/sftp, Windows Terminal e WinSCP: un client non sicuro risulta non disponibile. Verificato sui client reali: keepassxc-cli, mstsc, ssh, `wt.exe` e WinSCP sicuri; la build di sviluppo sotto `D:\` no. *Resta:* il proprietario su Linux (da fare su Kubuntu, serve codice nativo).
- **S11.** Le prove hanno **confermato** i tre problemi nel `TextBox` di Avalonia 12: con «Mostra» la password si copiava negli appunti, l'automazione dell'interfaccia la esponeva in chiaro anche nascosta, la cronologia di annullamento ne teneva copie. Nuovo controllo `SecretTextBox` (aspetto invariato): niente annullamento, copia e taglio bloccati, valore vuoto per l'automazione. *Resta:* le stringhe immutabili create durante la digitazione (limite noto di SEC-02).
- **S12.** Log di Avalonia solo in Debug. Un errore imprevisto sul thread dell'interfaccia blocca l'app (credenziali azzerate) invece di chiuderla; un crash su un altro thread blocca il database e rimuove le credenziali temporanee prima di uscire; un errore imprevisto di un client viene mostrato nella barra di stato. *Non fatto:* esclusione del processo dai dump di Windows Error Reporting.
- **S13.** Database o key file su un percorso UNC o su un'unità di rete: avviso nella schermata di sblocco. Non vengono rifiutati, perché un NAS è un uso legittimo.
- **S14.** Oltre 256 livelli di gruppi annidati la lettura si ferma con «database non leggibile» invece di chiudere l'app per stack overflow.

**S16 il 22 ottobre 2026: in parte corretto.**

- **CI:** `actions/checkout` (v7.0.1) e `actions/setup-dotnet` (v6.0.0) fissate all'hash del commit, `persist-credentials: false`, immagini dei runner esplicite (`windows-2025`, `ubuntu-24.04`), KeePassXC a versione fissa (Chocolatey 2.7.12, Ubuntu 2.7.6). `.github/dependabot.yml` propone ogni settimana gli aggiornamenti di Actions e pacchetti NuGet, che passano dalla CI.
- **Pubblicazione:** [build/publish.ps1](../build/publish.ps1) firma con Authenticode i binari dell'app (`KXC-ConnectionManager.exe/.dll`, `Kxc.*.dll`, `kxc-askpass.exe`) se riceve l'impronta di un certificato di firma del codice, con marca temporale; con `-RequireSignature` si rifiuta di produrre pacchetti non firmati o firmati con un certificato non riconosciuto; per Linux `-GpgKey` produce una firma `.asc`; `publish/SHA256SUMS` riporta l'hash di ogni archivio. Provato con un certificato autofirmato temporaneo (7 binari firmati; `-RequireSignature` lo rifiuta correttamente; certificato poi rimosso).
- **Manca il certificato**, che è una scelta del titolare del progetto. Smart App Control accetta solo firme di autorità riconosciute; un certificato autofirmato non basta. Opzioni:
  - **SignPath Foundation**: firma gratuita per progetti open source (la licenza AGPL va bene), con firma eseguita nella loro pipeline a partire dal repository pubblico;
  - **Azure Artifact Signing** (ex Trusted Signing) di Microsoft: servizio a pagamento mensile, con verifica dell'identità (da verificare la disponibilità per un privato in Italia);
  - **certificato OV di un'autorità** (Sectigo, DigiCert e altre): pagamento annuale, chiave privata su token o HSM.
- `build/icons/icon.cs` scarica SkiaSharp fuori dai lock file: è solo uno strumento di sviluppo e non entra nei pacchetti, quindi resta così.

**S15 e S17 il 23 ottobre 2026.**

- **S15: corretto, da provare su Kubuntu.** [LinuxSessionEvents.cs](../src/Kxc.Platform/Session/LinuxSessionEvents.cs) blocca l'app con i segnali D-Bus di più fonti, perché gli ambienti desktop non usano tutti la stessa:
  - logind: `PrepareForSleep(true)` per la sospensione; per il blocco, `Lock` e `LockedHint` della sessione dell'app (trovata con `GetSession("auto")`, poi con `XDG_SESSION_ID`); i segnali delle altre sessioni sono ignorati;
  - salvaschermo di KDE (`org.freedesktop.ScreenSaver.ActiveChanged`) e di GNOME (`org.gnome.ScreenSaver.ActiveChanged`), accettati solo dal proprietario del nome sul bus.

  La libreria è `Tmds.DBus.Protocol` 0.94.1 (MIT), la stessa che Avalonia porta già con sé: nessun codice di terze parti nuovo. Una fonte che manca (niente logind, nessun salvaschermo, bus non raggiungibile) non impedisce di ascoltare le altre. I test avviano un `dbus-daemon` privato con un client che impersona logind e i salvaschermi. Coprono:
  - sospensione e risveglio;
  - i due salvaschermi;
  - `Lock` e `LockedHint`;
  - le altre sessioni;
  - un mittente impostore;
  - servizi o bus mancanti.

  Girano solo su Linux, quindi nella CI su Ubuntu. *Resta:* la prova sul desktop reale di Kubuntu (Meta+L, `loginctl lock-session`, sospensione).
- **S17: verificato, nessun problema.** OpenSSH per Windows avvia `SSH_ASKPASS` con il percorso tra virgolette. La prova usa:
  - come helper, una copia di `cmd.exe` nella cartella `a b`;
  - come esca, una copia di `whoami.exe` chiamata `a.exe`, cioè il file che Windows proverebbe per primo con un percorso senza virgolette;
  - una chiave cifrata con la riga che stampa l'helper.

  `ssh-keygen -y` legge la chiave, quindi è partito l'helper giusto; con l'esca come helper fallisce. La prova è ripetuta da un test (`OpenSshAskPassPathTests`). ssh e ssh-keygen usano lo stesso codice per avviare l'helper.

## Piano di correzione

| Fase | Contenuto | Perché prima |
| --- | --- | --- |
| **1 · Subito** | S1 (utente consentito per SSH, titolo ripulito), S2 (verifica UUID, ambiguità con le scadute), S9 (`--` per keepassxc-cli) | Le due vulnerabilità confermate: eseguono comandi o mandano password all'host sbagliato a partire dal solo contenuto del database |
| **2 · Prima della distribuzione** | S3 (domanda legata a `utente@host`, `ForwardAgent=no`), S4 (pulizia non bloccante), S5 (controllo della cartella dell'app), S7 (credenziale RDP esistente), S10 (token di blocco) | Riguardano l'uso quotidiano e l'installazione su PC con più utenti |
| **3 · Rafforzamento** | S6 (flusso filtrante dell'export), S8 (controllo eseguibili completo), S11, S12, S13, S14 | Difesa in profondità sui segreti in memoria e sugli eseguibili |
| **4 · Con la versione Linux e i rilasci** | S15, S16, S17 | Legati a Kubuntu e alla pubblicazione dei pacchetti |

Ogni correzione va con il suo test (indicato nella scheda) e con la rilettura della checklist di sicurezza prevista dalla specifica. Per S1 e S2 conviene anche un test di regressione che riproduca esattamente le prove di questo documento.

## Prove eseguite per questa analisi

| Prova | Esito |
| --- | --- |
| Argomenti passati a una sonda tramite `wt.exe` (nome utente con virgolette) | L'utente viene spezzato e ssh riceverebbe `-oProxyCommand=calc` (S1) |
| `keepassxc-cli show` con voce scaduta e voce valida omonime | Restituita la password della voce scaduta (S2) |
| `keepassxc-cli show` con titolo `--all`, con e senza `--` | Senza `--` fallisce; con `--` funziona (S9) |
| `keepassxc-cli show` con `Server` e `server` | Maiuscole e minuscole distinte: coerente con il controllo delle ambiguità |
| `dotnet list package --vulnerable --include-transitive` | Nessun pacchetto vulnerabile |

Le prove usano solo database temporanei con dati inventati e una sonda che scrive su file gli argomenti ricevuti: nessun comando è stato eseguito.
