using System.Security.Cryptography;

namespace SmartMove.Core;

public sealed class SmartMover
{
    private readonly SmartMoveOptions _opt;
    private readonly IConflictPrompt _prompt;
    private readonly IFileDeleter _deleter;
    private readonly IFileCopier _copier;
    private ConflictAction? _stickyConflict;
    private ErrorAction? _stickyError;
    private int _filesTotal, _filesDone;
    private long _bytesTotal, _bytesDone;
    private Operation _op;
    private JobControl _ctl = new();
    private IProgress<Progress>? _progress;
    private string _current = "";

    public SmartMover(SmartMoveOptions opt, IConflictPrompt prompt, IFileDeleter? deleter = null, IFileCopier? copier = null)
    {
        _opt = opt; _prompt = prompt; _deleter = deleter ?? new PermanentDeleter(); _copier = copier ?? new ManagedCopier();
    }

    public MoveResult Run(IEnumerable<string> sources, string destDir, IProgress<Progress>? progress = null,
                          JobControl? ctl = null, Operation op = Operation.Move)
    {
        var result = new MoveResult();
        _op = op; _ctl = ctl ?? new JobControl(); _progress = progress;
        destDir = Path.GetFullPath(destDir);
        var list = sources.Select(Path.GetFullPath).ToList();
        try
        {
            Directory.CreateDirectory(destDir);
            _filesTotal = list.Sum(CountFiles); _bytesTotal = list.Sum(SizeOf);
            var fatal = Preflight(list, destDir);
            if (fatal != null) { result.Fatal = fatal; return result; }

            foreach (var s in list)
            {
                _ctl.Checkpoint();
                var target = Path.Combine(destDir, Path.GetFileName(s));
                if (Directory.Exists(s)) DoDirectory(s, target, result);
                else if (File.Exists(s)) DoFile(new FileInfo(s), target, result);
                else result.Items.Add(new ItemResult(s, null, ItemOutcome.Failed, "Non trovato"));
            }
        }
        catch (OperationCanceledException) { result.Cancelled = true; }
        return result;
    }

    // ---------- controlli preliminari ----------
    private string? Preflight(List<string> sources, string destDir)
    {
        foreach (var s in sources)
            if (Directory.Exists(s) && IsInside(destDir, s) && !Same(s, destDir) || (Directory.Exists(s) && Same(s, destDir)))
                return $"Impossibile {(_op == Operation.Copy ? "copiare" : "spostare")} la cartella «{Path.GetFileName(s)}» dentro se stessa.";
        if (_op == Operation.Copy || sources.Any(s => !SameVolume(s, destDir)))
        {
            try
            {
                var di = new DriveInfo(Path.GetPathRoot(destDir)!);
                if (di.IsReady && di.AvailableFreeSpace < _bytesTotal)
                    return $"Spazio insufficiente nella destinazione (servono {Fmt(_bytesTotal)}, disponibili {Fmt(di.AvailableFreeSpace)}).";
            }
            catch { /* percorsi di rete: non controllabile */ }
        }
        return null;
    }

    private static bool IsFat32(string dir)
    {
        try { var di = new DriveInfo(Path.GetPathRoot(dir)!); return di.IsReady && di.DriveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    // ---------- cartelle ----------
    private void DoDirectory(string src, string dst, MoveResult r)
    {
        if (Same(src, dst)) { if (_op == Operation.Move) return; dst = UniqueCopyName(dst, true); }
        if (File.Exists(dst)) dst = UniqueWindows(dst);
        else if (Directory.Exists(dst) && !Same(src, dst))
        {
            if (!_prompt.ConfirmMerge(src, dst)) throw new OperationCanceledException();
        }
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src))
        {
            _ctl.Checkpoint();
            DoFile(new FileInfo(f), Path.Combine(dst, Path.GetFileName(f)), r);
        }
        foreach (var d in Directory.EnumerateDirectories(src))
        {
            if (Same(d, dst)) continue;   // destinazione dentro la sorgente
            DoDirectory(d, Path.Combine(dst, Path.GetFileName(d)), r);
        }
        if (_op == Operation.Move)
            try { if (!Directory.EnumerateFileSystemEntries(src).Any()) { new DirectoryInfo(src).Attributes = FileAttributes.Normal; Directory.Delete(src); } } catch { }
        else
            try { var s = new DirectoryInfo(src); var d = new DirectoryInfo(dst); d.CreationTimeUtc = s.CreationTimeUtc; d.LastWriteTimeUtc = s.LastWriteTimeUtc; } catch { }
    }

    // ---------- file ----------
    private void DoFile(FileInfo src, string dst, MoveResult r)
    {
        _current = src.FullName; Report(0);
        long len = SafeLen(src);
        try
        {
            while (true)
            {
                try { DoFileOnce(src, dst, r); break; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    var act = _stickyError ?? AskError(src.FullName, ex.Message);
                    if (act == ErrorAction.Retry) continue;
                    r.Items.Add(new ItemResult(src.FullName, dst, ItemOutcome.Failed, ex.Message));
                    break;
                }
            }
        }
        finally
        {
            _filesDone++;
            _bytesDone = Math.Min(_bytesTotal, _bytesDone + 0); // gli incrementi sono già contati in Report
            _fileBase += len; Report(0);
        }
    }
    private long _fileBase; // byte dei file già completati (anche saltati)

    private void DoFileOnce(FileInfo src, string dst, MoveResult r)
    {
        _ctl.Checkpoint();
        bool sameFolder = Same(Path.GetDirectoryName(src.FullName)!, Path.GetDirectoryName(dst)!);
        if (Same(src.FullName, dst))
        {
            if (_op == Operation.Move) return;                            // sorgente == destinazione
            var u = UniqueCopyName(dst, false); Transfer(src, u, false);
            r.Items.Add(new ItemResult(src.FullName, u, ItemOutcome.Copied)); return;
        }
        if (!File.Exists(dst) && !Directory.Exists(dst))
        {
            Transfer(src, dst, false);
            r.Items.Add(new ItemResult(src.FullName, dst, _op == Operation.Copy ? ItemOutcome.Copied : ItemOutcome.Moved));
            return;
        }
        if (Directory.Exists(dst))
        {
            var u = UniqueWindows(dst); Transfer(src, u, false);
            r.Items.Add(new ItemResult(src.FullName, u, ItemOutcome.MovedRenamed)); return;
        }
        var dstInfo = new FileInfo(dst);
        var action = _stickyConflict ?? AskOrCancel(src, dstInfo);
        Apply(action, src, dstInfo, r);
    }

    private ConflictAction AskOrCancel(FileInfo s, FileInfo d)
    {
        var c = _prompt.Ask(s, d);
        if (c.Action == ConflictAction.Cancel) throw new OperationCanceledException();
        if (c.ApplyToAll) _stickyConflict = c.Action;
        return c.Action;
    }

    private ErrorAction AskError(string path, string msg)
    {
        var c = _prompt.AskError(path, msg);
        if (c.Action == ErrorAction.Cancel) throw new OperationCanceledException();
        if (c.ApplyToAll && c.Action == ErrorAction.Skip) _stickyError = ErrorAction.Skip;
        return c.Action;
    }

    private void Apply(ConflictAction a, FileInfo src, FileInfo dst, MoveResult r)
    {
        switch (a)
        {
            case ConflictAction.Skip:
                r.Items.Add(new ItemResult(src.FullName, dst.FullName, ItemOutcome.Skipped)); break;
            case ConflictAction.Overwrite:
                Transfer(src, dst.FullName, true);
                r.Items.Add(new ItemResult(src.FullName, dst.FullName, ItemOutcome.Overwritten)); break;
            case ConflictAction.OverwriteIfNewer:
                if (src.LastWriteTimeUtc > dst.LastWriteTimeUtc) { Transfer(src, dst.FullName, true); r.Items.Add(new ItemResult(src.FullName, dst.FullName, ItemOutcome.Overwritten)); }
                else r.Items.Add(new ItemResult(src.FullName, dst.FullName, ItemOutcome.Skipped));
                break;
            case ConflictAction.KeepBoth:
                { var u = UniqueWindows(dst.FullName); Transfer(src, u, false); r.Items.Add(new ItemResult(src.FullName, u, ItemOutcome.MovedRenamed)); }
                break;
            case ConflictAction.SmartMove:
                if (IsSameFile(src, dst))
                {
                    // Move: l'origine è un duplicato -> via. Copy: nulla da copiare, la destinazione ha già lo stesso file.
                    if (_op == Operation.Move) _deleter.Delete(src.FullName);
                    r.Items.Add(new ItemResult(src.FullName, dst.FullName, _op == Operation.Move ? ItemOutcome.SourceDeletedAsDuplicate : ItemOutcome.Skipped));
                }
                else
                {
                    var u = UniquePath(dst.FullName); Transfer(src, u, false);
                    r.Items.Add(new ItemResult(src.FullName, u, ItemOutcome.MovedRenamed));
                }
                break;
        }
    }

    public bool IsSameFile(FileInfo a, FileInfo b)
    {
        if (a.Length != b.Length) return false;
        if (_opt.CompareField == TimeField.None) return true;   // solo dimensione esatta in byte
        var ta = _opt.CompareField == TimeField.CreationTime ? a.CreationTimeUtc : a.LastWriteTimeUtc;
        var tb = _opt.CompareField == TimeField.CreationTime ? b.CreationTimeUtc : b.LastWriteTimeUtc;
        return Math.Abs((ta - tb).TotalSeconds) <= _opt.ToleranceSeconds;
    }

    // ---------- nomi ----------
    /// <summary>Regola smart: nome.ext -> nome_2.ext, nome_3.ext... primo libero.</summary>
    public static string UniquePath(string path) => Unique(path, (stem, n) => $"{stem}_{n}");
    /// <summary>Stile Windows "mantieni entrambi": nome (2).ext</summary>
    public static string UniqueWindows(string path) => Unique(path, (stem, n) => $"{stem} ({n})");
    /// <summary>Copia nella stessa cartella: "nome - Copia.ext", poi "nome - Copia (2).ext"</summary>
    public static string UniqueCopyName(string path, bool isDir)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var stem = isDir ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        var ext = isDir ? "" : Path.GetExtension(path);
        var c = Path.Combine(dir, $"{stem} - Copia{ext}");
        if (!File.Exists(c) && !Directory.Exists(c)) return c;
        for (int n = 2; ; n++)
        {
            c = Path.Combine(dir, $"{stem} - Copia ({n}){ext}");
            if (!File.Exists(c) && !Directory.Exists(c)) return c;
        }
    }
    private static string Unique(string path, Func<string, int, string> fmt)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int n = 2; ; n++)
        {
            var c = Path.Combine(dir, fmt(stem, n) + ext);
            if (!File.Exists(c) && !Directory.Exists(c)) return c;
        }
    }

    // ---------- trasferimento fisico ----------
    private void Transfer(FileInfo src, string dst, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        if (File.Exists(dst) && overwrite) { var d = new FileInfo(dst); if (d.IsReadOnly) d.IsReadOnly = false; }
        if (new FileInfo(src.FullName).Length >= 4L * 1024 * 1024 * 1024 - 1 && IsFat32(Path.GetDirectoryName(dst)!))
            throw new IOException("File più grande di 4 GB: la destinazione è FAT32");

        if (_op == Operation.Move && SameVolume(src.FullName, dst))
        {
            File.Move(src.FullName, dst, overwrite);   // rinomina: conserva tutte le date
            return;
        }
        var ct = src.CreationTimeUtc; var wt = src.LastWriteTimeUtc; var attr = src.Attributes; var len = src.Length;
        long baseBytes = _fileBase;
        _copier.Copy(src.FullName, dst, overwrite, done => { _bytesDone = baseBytes + done; Report(done); }, _ctl);
        var n = new FileInfo(dst);
        if (n.IsReadOnly) n.IsReadOnly = false;
        File.SetCreationTimeUtc(dst, ct);            // la regola Smart si basa sulla data di creazione
        File.SetLastWriteTimeUtc(dst, wt);
        if (new FileInfo(dst).Length != len) throw new IOException("Verifica dimensione fallita dopo la copia");
        if (_opt.VerifyAfterCopy && !HashEq(src.FullName, dst)) { try { File.Delete(dst); } catch { } throw new IOException("Verifica hash fallita"); }
        File.SetAttributes(dst, attr);
        if (_op == Operation.Move)
        {
            if (src.IsReadOnly) src.IsReadOnly = false;
            File.Delete(src.FullName);
        }
    }

    private static bool HashEq(string a, string b)
    {
        using var s = SHA256.Create();
        using var fa = File.OpenRead(a); using var fb = File.OpenRead(b);
        return s.ComputeHash(fa).AsSpan().SequenceEqual(s.ComputeHash(fb));
    }

    private void Report(long fileBytes) =>
        _progress?.Report(new Progress(_current, _filesDone, _filesTotal, Math.Min(_bytesTotal, _fileBase + fileBytes), _bytesTotal));

    // ---------- utilità ----------
    private static bool IsInside(string child, string parent) =>
        Path.GetFullPath(child).TrimEnd('\\', '/').StartsWith(Path.GetFullPath(parent).TrimEnd('\\', '/') + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool SameVolume(string a, string b)
    {
        if (!OperatingSystem.IsWindows()) return true;
        return string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    }
    public static bool Same(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static long SafeLen(FileInfo f) { try { return f.Length; } catch { return 0; } }
    private static int CountFiles(string p) => Directory.Exists(p) ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Count() : 1;
    private static long SizeOf(string p) =>
        Directory.Exists(p) ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Sum(f => SafeLen(new FileInfo(f))) : SafeLen(new FileInfo(p));
    private static string Fmt(long b) => b < 1048576 ? $"{b / 1024.0:F0} KB" : b < 1073741824 ? $"{b / 1048576.0:F1} MB" : $"{b / 1073741824.0:F2} GB";
}
