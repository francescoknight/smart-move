# Smart Move per Windows

Gestore di copia/spostamento in stile TeraCopy per Esplora file, con la regola **Smart Move** e tutte le opzioni della copia di Windows.

## Cosa fa
Smart Move interviene **solo sullo spostamento**. La copia resta a Windows.

| In Esplora | Gestita da | Come |
|---|---|---|
| **Ctrl+X → Ctrl+V** (taglia e incolla) | Smart Move | processo residente con hook tastiera |
| **Ctrl+C → Ctrl+V** (copia) | Windows | l'hook controlla gli appunti e lascia passare |
| **Maiusc + trascinamento** su una cartella | Smart Move (sperimentale) | drop handler, `install_drop.bat` |
| Trascinamento normale / Ctrl / Alt su una cartella | Windows (stesso motore di Esplora, `SHFileOperation`) | il drop handler lancia `SmartMove.exe ... native` |
| Trascinamento **tasto destro** → *Smart Move qui / Smart Copy qui* | Smart Move | voci esplicite del menu |
| Tasto destro nello sfondo → *Incolla (Smart Move)* | Smart Move | voce esplicita |

Nota: con `install_drop.bat` il drop handler sostituisce quello di Windows per **tutti** i trascinamenti sulle cartelle. Per non cambiarne il comportamento, quelli senza Maiusc sono rieseguiti dal motore nativo di Windows (stesse finestre di conferma). Se noti differenze: `uninstall_drop.bat` ripristina tutto.

### Regola Smart Move (nel dialogo di conflitto)
Stesso nome nella destinazione: se ha **la stessa dimensione esatta in byte** (la data non conta; in `config.json` `CompareField` può essere `CreationTime` o `LastWriteTime` per controllare anche la data, tolleranza 2 s) l'origine viene eliminata (Cestino); altrimenti l'origine viene spostata come `nome_2.ext`, `_3`, `_4`… In copia, un file identico semplicemente non viene ricopiato.

### Opzioni come in Windows
- Conflitto: **Sostituisci**, **Ignora**, **Scegli i file da conservare** (origine/destinazione/entrambi → `nome (2).ext`), più *Sostituisci se più recente*, *Smart* e **"Esegui per tutti"**.
- Copia nella stessa cartella: `nome - Copia.ext`, `nome - Copia (2).ext`.
- Unione di cartelle esistenti (con conferma); cartelle sorgente svuotate rimosse nello spostamento.
- Errori (file in uso, accesso negato): **Riprova / Ignora / Annulla**, con "ignora per tutti".
- Pausa / Riprendi / Annulla, velocità, tempo rimanente, coda dei lavori (uno alla volta).
- Controlli preliminari: spazio libero, file >4 GB su FAT32, cartella dentro se stessa.
- Date di creazione/modifica e attributi conservati (anche tra dischi diversi), flussi NTFS alternativi e percorsi lunghi (CopyFileEx).
- Verifica opzionale con SHA-256 (`VerifyAfterCopy`).

## Installazione
1. Scarica l'artefatto del workflow GitHub Actions (o esegui `build.bat`) → cartella `dist`. Serve il runtime .NET 8 Desktop.
2. `install.bat` (nessun admin). Facoltativo: `install_drop.bat` per il trascinamento col sinistro.
3. Rimozione: `uninstall.bat`. L'icona nell'area di notifica ha "Intercetta Ctrl+V" per disattivare al volo l'intercettazione.

Configurazione: `%APPDATA%\SmartMove\config.json` (`CompareField`, `ToleranceSeconds`, `UseRecycleBin`, `VerifyAfterCopy`, `InterceptPaste`).

## Limiti noti — leggere
- **Ctrl+V**: viene intercettato solo se la finestra attiva è Esplora/Desktop, il focus non è in una casella di testo e negli appunti ci sono file. Negli altri casi (immagini, allegati Outlook, Questo PC, Cestino, cartelle zip, Libreria) passa a Esplora. Windows 11 con **più schede** nella stessa finestra: la cartella è ambigua, quindi lascia fare a Esplora.
- **Drop handler (sinistro)**, comportamento scelto: i trascinamenti che **non** contengono file (immagini da browser, testo) su una cartella vengono **rifiutati**; Alt e Ctrl+Maiusc (collegamenti) sono rifiutati. Per sicurezza sui dati, dichiara sempre "copia" a Esplora e fa lo spostamento il motore, così Esplora non cancella gli originali mentre sono ancora in copia. **Non so se Esplora usa questo handler anche per i rilasci sullo sfondo della finestra aperta** (potrebbe valere solo per le icone di cartella). Se dà problemi: `uninstall_drop.bat`.
- Ctrl+Z (annulla) di Esplora non copre le operazioni di Smart Move. Le richieste di elevazione ("Continua" come amministratore) non sono gestite: il file viene ignorato.
- Le date: spostando tra dischi la copia riceve una nuova data di creazione; Smart Move la ripristina. File copiati con altri strumenti possono avere date diverse → usa `LastWriteTime` in config.

## Stato di verifica
- **Verificato qui (Linux):** motore (`SmartMove.Core`) con 23 test xUnit; compilazione completa del codice C# dell'app (targeting pack Windows); compilazione della DLL con mingw-w64.
- **Mai eseguito su Windows:** hook Ctrl+V, drop handler, handler tasto destro, finestre WinForms, CopyFileEx, Cestino. Aspettati correzioni al primo test reale. Il workflow CI compila su `windows-latest` ma non testa l'interfaccia.

## Aggiornamenti
Ogni modifica passa dai test su Windows e viene pubblicata in https://github.com/francescoknight/smart-move/releases/tag/latest.
Per aggiornare: doppio clic su `update.bat` (nella cartella di SmartMove.exe) oppure icona nell'area di notifica → **Cerca aggiornamenti**.
Lo script chiude Smart Move, sostituisce i file (la DLL usata da Esplora viene rinominata), registra i componenti e riavvia Esplora file solo se la DLL è cambiata.

## Trova foto duplicate
Tasto destro su una cartella (o sul suo sfondo) → **Trova foto duplicate da questa cartella** (su Win11: "Mostra altre opzioni"). Cerca nelle sottocartelle file con la **stessa dimensione esatta in byte** (file vuoti ignorati; per default solo immagini).

- **Blocchi:** ogni gruppo di copie è un blocco con intestazione ("BLOCCO 3 — 5 copie × 2,3 MB — recuperabili 9,2 MB"), comprimibile. Ordinabili per spazio recuperabile, dimensione del file, numero di copie o nome.
- **Evidenziazione:** arancio = data di creazione o estensione diversa; verde = stessa data. La colonna *Stato* dice "★ originale consigliato", "doppione sicuro" o "solo stessa dimensione".
- **Selezione:** caselle su ogni file, menu *Seleziona* (duplicati sicuri / tutti i duplicati / tutto / niente / inverti), tasto destro per spuntare le righe selezionate o un intero blocco tranne l'originale consigliato, Spazio per spuntare, Ctrl+A.
- **Eliminazione (sempre nel Cestino, con conferma):** *Elimina duplicati SICURI* (stessa dimensione + stessa data di creazione), *Elimina TUTTI i duplicati* (solo dimensione), *Elimina spuntati*. In ogni blocco resta sempre almeno una copia. L'originale consigliato evita nomi tipo `(2)`, `_2`, `- Copia`, poi la data più vecchia, poi il percorso più corto.
- Doppio clic = mostra nella cartella; esportazione CSV.
