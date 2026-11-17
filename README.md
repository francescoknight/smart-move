# Smart Move per Windows

Gestore di copia/spostamento in stile TeraCopy per Esplora file, con la regola **Smart Move** e tutte le opzioni della copia di Windows.

## Cosa fa
| Azione in Esplora | Gestita da Smart Move | Come |
|---|---|---|
| **Ctrl+V** (incolla copiati/tagliati) | ✔ | processo residente con hook tastiera |
| Tasto destro → *Incolla (Smart Move)* | ✔ | voce di menu (su Win11: "Mostra altre opzioni") |
| Trascinamento **tasto destro** → *Smart Move qui / Smart Copy qui* | ✔ | handler di trascinamento |
| Trascinamento **tasto sinistro** su una cartella | ✔ (sperimentale) | drop handler, `install_drop.bat` |

Trascinando col sinistro: Ctrl = copia, Maiusc = sposta, altrimenti stesso disco = sposta, disco diverso = copia (come Esplora).

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
Tasto destro su una cartella (o sul suo sfondo) → **Trova foto duplicate da questa cartella** (su Win11: "Mostra altre opzioni"). Cerca nelle sottocartelle file con la **stessa dimensione esatta in byte** (file vuoti ignorati; per default solo immagini, deselezionabile). Il riepilogo mostra nome, estensione, dimensione, data di creazione/modifica e cartella; i gruppi con data di creazione o estensione diverse sono evidenziati in arancio, quelli con la stessa data in verde. Doppio clic = mostra il file in Esplora; esportazione CSV. Non cancella nulla.
