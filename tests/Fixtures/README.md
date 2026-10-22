# Fixture dei test

Solo dati inventati: mai credenziali reali, mai copie dei database personali (che stanno in
`TestData/`, escluso dal repository).

- `fixture.xml`: sorgente della fixture in formato XML di KeePass.
- `fixture.kdbx`: database generato dall'XML. keepassxc-cli 2.7.12 crea solo KDBX 3.1 (AES-KDF).
  Password `test`, key file `fixture.keyx`.
- `fixture.keyx`: key file generato apposta per le fixture, non usato da nessun altro database.
- `fixture-casi.xml` / `fixture-casi.kdbx`: casi limite dell'analisi di sicurezza (voce scaduta con lo
  stesso percorso di una valida, titolo `--all`), stessa password e stesso key file. Si rigenera
  come `fixture.kdbx`, sostituendo i nomi dei file.
- `fixture-kdbx4.kdbx` (facoltativo): KDBX 4 con Argon2, per il test di sblocco KDBX 4. Se manca,
  quel test viene saltato.

Per rigenerare `fixture.kdbx` dopo una modifica all'XML (il key file resta lo stesso):

```sh
printf 'test\ntest\n' | keepassxc-cli import -q -p --set-key-file fixture.keyx -t 100 fixture.xml fixture.kdbx
```

Per creare `fixture-kdbx4.kdbx` nella GUI di KeePassXC: nuovo database in formato KDBX 4 con
Argon2, password `test`, key file `fixture.keyx` (scegli il file esistente), una sola voce dal
titolo `Prova` con utente e password inventati.

Casi coperti da `fixture.xml`: tag in maiuscolo e misti (`RDP`, `SSH;Sftp`, `rdp,ssh`), campi
personalizzati `Host`/`Port`/`Domain`, host ricavato dall'URL, voce scaduta, voce nel Cestino, voce
senza host, voce con host e utente che iniziano con `-`, titolo con virgolette e `/`, password con
caratteri speciali, campo personalizzato protetto, allegato e cronologia della voce.
