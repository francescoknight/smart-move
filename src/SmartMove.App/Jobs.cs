using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using SmartMove.Core;

namespace SmartMove.App;

sealed class JobRequest
{
    public List<string> Sources { get; set; } = new();
    public string Dest { get; set; } = "";
    public Operation Op { get; set; }
    public bool ClearClipboardWhenDone { get; set; }
}

/// <summary>Coda di lavori (uno alla volta) + server su named pipe per l'istanza unica.</summary>
sealed class JobService : IDisposable
{
    public const string PipeName = "SmartMove.Jobs.v1";
    readonly BlockingCollection<JobRequest> _q = new();
    readonly Control _ui; readonly SmartMoveOptions _opt;
    readonly CancellationTokenSource _stop = new();
    public event Action? Idle;   // coda vuota
    int _pending;

    public JobService(Control ui, SmartMoveOptions opt) { _ui = ui; _opt = opt; new Thread(Worker) { IsBackground = true, Name = "jobs" }.Start(); }

    public void Enqueue(JobRequest j) { Interlocked.Increment(ref _pending); _q.Add(j); }

    void Worker()
    {
        foreach (var j in _q.GetConsumingEnumerable(_stop.Token))
        {
            try { Run(j); } catch (Exception ex) { _ui.BeginInvoke(() => MessageBox.Show(ex.Message, "Smart Move - errore")); }
            if (Interlocked.Decrement(ref _pending) == 0) _ui.BeginInvoke(() => Idle?.Invoke());
        }
    }

    void Run(JobRequest j)
    {
        var ctl = new JobControl();
        ProgressForm? form = null;
        var destName = Path.GetFileName(j.Dest.TrimEnd('\\')); if (destName.Length == 0) destName = j.Dest;
        var title = (j.Op == Operation.Copy ? "Copia" : "Spostamento") + $" di {j.Sources.Count} elementi in {destName}";
        _ui.Invoke(() => { form = new ProgressForm(title, ctl, _q.Count); form.Show(); });
        var prompt = new DialogPrompt(_ui, form!);
        IFileDeleter del = _opt.UseRecycleBin ? new RecycleBinDeleter() : new PermanentDeleter();
        var mover = new SmartMover(_opt, prompt, del, new WinCopier());
        var prog = new Throttled(p => _ui.BeginInvoke(() => form!.UpdateProgress(p)));
        var res = mover.Run(j.Sources, j.Dest, prog, ctl, j.Op);
        _ui.Invoke(() => { form!.Close(); form.Dispose(); });
        if (j.ClearClipboardWhenDone && !res.Cancelled && res.Failures == 0) _ui.Invoke(() => { try { Clipboard.Clear(); } catch { } });
        _ui.Invoke(() => Summary(res));
    }

    static void Summary(MoveResult r)
    {
        if (r.Fatal != null) { MessageBox.Show(r.Fatal, "Smart Move", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (r.Failures == 0 && !r.Cancelled) return;
        var sb = new System.Text.StringBuilder();
        if (r.Cancelled) sb.AppendLine("Operazione annullata.");
        sb.AppendLine($"Elaborati: {r.Items.Count - r.Failures - r.Count(ItemOutcome.Skipped)}   Ignorati: {r.Count(ItemOutcome.Skipped)}   Errori: {r.Failures}");
        foreach (var f in r.Items.Where(i => i.Outcome == ItemOutcome.Failed).Take(15)) sb.AppendLine($"• {Path.GetFileName(f.Source)}: {f.Error}");
        MessageBox.Show(sb.ToString(), "Smart Move");
    }

    // --- pipe ---
    public void StartPipeServer() => new Thread(() =>
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte);
                s.WaitForConnection();
                using var rd = new StreamReader(s);
                var j = JsonSerializer.Deserialize<JobRequest>(rd.ReadToEnd());
                if (j != null) Enqueue(j);
            }
            catch { Thread.Sleep(200); }
        }
    }) { IsBackground = true }.Start();

    public static bool TrySendToRunning(JobRequest j)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            c.Connect(300);
            using var w = new StreamWriter(c); w.Write(JsonSerializer.Serialize(j)); w.Flush();
            return true;
        }
        catch { return false; }
    }

    public void Dispose() { _stop.Cancel(); _q.CompleteAdding(); }

    sealed class Throttled(Action<SmartMove.Core.Progress> a) : IProgress<SmartMove.Core.Progress>
    {
        long _last;
        public void Report(SmartMove.Core.Progress p)
        {
            var now = Environment.TickCount64;
            if (now - _last < 100 && p.BytesDone < p.BytesTotal) return;
            _last = now; a(p);
        }
    }
}

sealed class RecycleBinDeleter : IFileDeleter
{
    public void Delete(string path) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
}
