namespace SmartMove.Core;

public enum Operation { Copy, Move }

/// <summary>Azione scelta in caso di conflitto (nome già presente nella destinazione).</summary>
public enum ConflictAction
{
    /// <summary>Regola "smart": stesso nome+dimensione+data => elimina sorgente; altrimenti sposta rinominando _2, _3...</summary>
    SmartMove,
    /// <summary>Sostituisci il file nella destinazione.</summary>
    Overwrite,
    /// <summary>Ignora: lascia l'origine dov'è.</summary>
    Skip,
    /// <summary>Mantieni entrambi, nomi alla Windows: nome (2).ext</summary>
    KeepBoth,
    OverwriteIfNewer,
    Cancel
}

public enum ErrorAction { Retry, Skip, Cancel }

public enum TimeField { CreationTime, LastWriteTime }

public sealed class SmartMoveOptions
{
    public TimeField CompareField { get; set; } = TimeField.CreationTime;
    /// <summary>Tolleranza in secondi (FAT/exFAT/NAS hanno risoluzione di 2 s).</summary>
    public double ToleranceSeconds { get; set; } = 2.0;
    /// <summary>Duplicati eliminati / origini dei move: Cestino (recuperabile) invece di cancellazione definitiva.</summary>
    public bool UseRecycleBin { get; set; } = true;
    /// <summary>Dopo la copia confronta l'hash SHA-256 di origine e destinazione (più lento).</summary>
    public bool VerifyAfterCopy { get; set; } = false;
    /// <summary>Intercetta Ctrl+V in Esplora file (processo tray).</summary>
    public bool InterceptPaste { get; set; } = true;
}

public sealed record ConflictChoice(ConflictAction Action, bool ApplyToAll);
public sealed record ErrorChoice(ErrorAction Action, bool ApplyToAll);

public interface IConflictPrompt
{
    ConflictChoice Ask(FileInfo source, FileInfo destination);
    /// <summary>Errore (accesso negato, file in uso...). Predefinito: ignora il file.</summary>
    ErrorChoice AskError(string path, string message) => new(ErrorAction.Skip, false);
    /// <summary>La cartella esiste già nella destinazione: unire? Predefinito: sì.</summary>
    bool ConfirmMerge(string sourceDir, string destDir) => true;
}

public interface IFileDeleter { void Delete(string path); }

public sealed class PermanentDeleter : IFileDeleter
{
    public void Delete(string path)
    {
        var fi = new FileInfo(path);
        if (fi.Exists && fi.IsReadOnly) fi.IsReadOnly = false;
        File.Delete(path);
    }
}

/// <summary>Copia fisica di un file con avanzamento (byte copiati del file corrente).</summary>
public interface IFileCopier
{
    void Copy(string src, string dst, bool overwrite, Action<long> onBytes, JobControl ctl);
}

public sealed record Progress(string CurrentFile, int FilesDone, int FilesTotal, long BytesDone, long BytesTotal);

public enum ItemOutcome { Copied, Moved, MovedRenamed, SourceDeletedAsDuplicate, Overwritten, Skipped, Failed }

public sealed record ItemResult(string Source, string? Destination, ItemOutcome Outcome, string? Error = null);

public sealed class MoveResult
{
    public List<ItemResult> Items { get; } = new();
    public bool Cancelled { get; set; }
    public string? Fatal { get; set; }
    public int Count(ItemOutcome o) => Items.Count(i => i.Outcome == o);
    public int Failures => Count(ItemOutcome.Failed);
}

/// <summary>Pausa / ripresa / annulla di un lavoro.</summary>
public sealed class JobControl
{
    private readonly ManualResetEventSlim _run = new(true);
    private readonly CancellationTokenSource _cts = new();
    public CancellationToken Token => _cts.Token;
    public bool IsPaused => !_run.IsSet;
    public void Pause() => _run.Reset();
    public void Resume() => _run.Set();
    public void Cancel() { _cts.Cancel(); _run.Set(); }
    /// <summary>Blocca finché in pausa; lancia OperationCanceledException se annullato.</summary>
    public void Checkpoint() { _run.Wait(_cts.Token); _cts.Token.ThrowIfCancellationRequested(); }
}
