using SmartMove.Core;

namespace SmartMove.App;

sealed class ProgressForm : Form
{
    readonly Label _title = new() { Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 6, 8, 0) };
    readonly Label _file = new() { Dock = DockStyle.Top, Height = 22, Padding = new Padding(8, 2, 8, 0), AutoEllipsis = true };
    readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 22, Maximum = 1000 };
    readonly Label _stats = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 0) };
    readonly Button _pause = new() { Text = "Pausa", Width = 90 };
    readonly Button _cancel = new() { Text = "Annulla", Width = 90 };
    readonly JobControl _ctl;
    readonly DateTime _start = DateTime.UtcNow;

    public ProgressForm(string title, JobControl ctl, int queued)
    {
        _ctl = ctl;
        Text = "Smart Move"; ClientSize = new Size(520, 170); StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; ShowInTaskbar = true;
        _title.Text = title + (queued > 0 ? $"   (in coda: {queued})" : "");
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        bottom.Controls.Add(_cancel); bottom.Controls.Add(_pause);
        Controls.Add(bottom); Controls.Add(_stats); Controls.Add(_bar); Controls.Add(_file); Controls.Add(_title);
        _pause.Click += (_, _) => { if (_ctl.IsPaused) { _ctl.Resume(); _pause.Text = "Pausa"; } else { _ctl.Pause(); _pause.Text = "Riprendi"; } };
        _cancel.Click += (_, _) => { _cancel.Enabled = false; _ctl.Cancel(); };
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing && _cancel.Enabled) { e.Cancel = true; _cancel.PerformClick(); } };
    }

    public void UpdateProgress(SmartMove.Core.Progress p)
    {
        _file.Text = Path.GetFileName(p.CurrentFile);
        _bar.Value = p.BytesTotal > 0 ? (int)Math.Min(1000, p.BytesDone * 1000 / p.BytesTotal) : 0;
        var secs = Math.Max(0.5, (DateTime.UtcNow - _start).TotalSeconds);
        var speed = p.BytesDone / secs;
        var eta = speed > 1 ? TimeSpan.FromSeconds((p.BytesTotal - p.BytesDone) / speed) : TimeSpan.Zero;
        _stats.Text = $"{Sz(p.BytesDone)} di {Sz(p.BytesTotal)}  ({_bar.Value / 10}%)   File: {p.FilesDone}/{p.FilesTotal}\n" +
                      $"Velocità: {Sz((long)speed)}/s   Tempo rimanente: {(eta.TotalHours >= 1 ? eta.ToString(@"h\:mm\:ss") : eta.ToString(@"m\:ss"))}";
    }
    internal static string Sz(long b) => b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024.0:F1} KB" : b < 1073741824 ? $"{b / 1048576.0:F1} MB" : $"{b / 1073741824.0:F2} GB";
}

/// <summary>Mostra i dialoghi sul thread UI (il motore gira su un thread di lavoro).</summary>
sealed class DialogPrompt(Control ui, Form owner) : IConflictPrompt
{
    public ConflictChoice Ask(FileInfo s, FileInfo d) => (ConflictChoice)ui.Invoke(() =>
    {
        using var f = new ConflictForm(s, d); f.ShowDialog(owner); return new ConflictChoice(f.Action, f.ApplyAll);
    });
    public ErrorChoice AskError(string path, string message) => (ErrorChoice)ui.Invoke(() =>
    {
        using var f = new ErrorForm(path, message); f.ShowDialog(owner); return new ErrorChoice(f.Action, f.ApplyAll);
    });
    public bool ConfirmMerge(string s, string d) => (bool)ui.Invoke(() =>
        MessageBox.Show(owner, $"La cartella «{Path.GetFileName(d)}» esiste già nella destinazione.\nVuoi unire il contenuto? I file con lo stesso nome richiederanno una scelta.",
            "Cartella già esistente", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
}

sealed class ErrorForm : Form
{
    public ErrorAction Action { get; private set; } = ErrorAction.Cancel;
    public bool ApplyAll => _all.Checked;
    readonly CheckBox _all = new() { Text = "Ignora per tutti gli errori successivi", AutoSize = true, Left = 12, Top = 100 };
    public ErrorForm(string path, string msg)
    {
        Text = "Errore"; ClientSize = new Size(480, 175); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        Controls.Add(new Label { Left = 12, Top = 10, Width = 456, Height = 80, Text = $"Impossibile elaborare:\n{path}\n\n{msg}" });
        Controls.Add(_all);
        void B(string t, int x, ErrorAction a) { var b = new Button { Text = t, Left = x, Top = 135, Width = 100 }; b.Click += (_, _) => { Action = a; Close(); }; Controls.Add(b); }
        B("Riprova", 12, ErrorAction.Retry); B("Ignora", 120, ErrorAction.Skip); B("Annulla", 368, ErrorAction.Cancel);
    }
}

sealed class ConflictForm : Form
{
    public ConflictAction Action { get; private set; } = ConflictAction.Cancel;
    public bool ApplyAll => _all.Checked;
    readonly CheckBox _all = new() { Text = "Esegui per tutti gli elementi in conflitto", AutoSize = true, Left = 12 };

    public ConflictForm(FileInfo s, FileInfo d)
    {
        Text = "Sostituisci o ignora file"; ClientSize = new Size(560, 420); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        Controls.Add(new Label
        {
            Left = 12, Top = 10, Width = 536, Height = 100,
            Text = $"La destinazione contiene già un file denominato «{d.Name}».\n\n" +
                   $"ORIGINE:       {ProgressForm.Sz(s.Length)}   creato {s.CreationTime:g}   modificato {s.LastWriteTime:g}\n" +
                   $"DESTINAZIONE:  {ProgressForm.Sz(d.Length)}   creato {d.CreationTime:g}   modificato {d.LastWriteTime:g}"
        });
        int y = 115;
        void B(string t, ConflictAction a, bool primary = false)
        {
            var b = new Button { Text = t, Left = 12, Top = y, Width = 536, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
            if (primary) { b.Font = new Font(b.Font, FontStyle.Bold); AcceptButton = b; }
            b.Click += (_, _) => { Action = a; Close(); };
            Controls.Add(b); y += 34;
        }
        B("★ Smart: se identico (nome, dimensione, data) elimina l'origine; altrimenti sposta come _2, _3…", ConflictAction.SmartMove, true);
        B("Sostituisci il file nella destinazione", ConflictAction.Overwrite);
        B("Ignora il file (resta nell'origine)", ConflictAction.Skip);
        B("Sostituisci solo se l'origine è più recente", ConflictAction.OverwriteIfNewer);
        B("Mantieni entrambi i file (nome (2).ext)", ConflictAction.KeepBoth);

        // "Scegli i file da conservare" come in Windows
        var g = new GroupBox { Left = 12, Top = y + 2, Width = 536, Height = 70, Text = "Scegli i file da conservare" };
        var cs = new CheckBox { Left = 12, Top = 20, Width = 500, Text = $"File dall'origine ({s.LastWriteTime:g})" };
        var cd = new CheckBox { Left = 12, Top = 42, Width = 500, Text = $"File già nella destinazione ({d.LastWriteTime:g})", Checked = true };
        var ok = new Button { Left = 440, Top = 36, Width = 88, Text = "Continua" };
        ok.Click += (_, _) => { Action = cs.Checked && cd.Checked ? ConflictAction.KeepBoth : cs.Checked ? ConflictAction.Overwrite : ConflictAction.Skip; Close(); };
        g.Controls.AddRange(new Control[] { cs, cd, ok }); Controls.Add(g);
        y += 80; _all.Top = y; Controls.Add(_all);
        var cancel = new Button { Text = "Annulla operazione", Left = 408, Top = y - 4, Width = 140, Height = 28 };
        cancel.Click += (_, _) => { Action = ConflictAction.Cancel; Close(); };
        Controls.Add(cancel); CancelButton = cancel;
        ClientSize = new Size(560, y + 36);
    }
}
