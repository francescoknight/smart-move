using System.Text;
using SmartMove.Core;

namespace SmartMove.App;

/// <summary>Riepilogo dei file con la stessa dimensione in byte. Evidenzia date di creazione ed estensioni diverse.</summary>
sealed class DupesForm : Form
{
    readonly string _root;
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, ShowGroups = true, HideSelection = false };
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(8, 6, 8, 0) };
    readonly CheckBox _images = new() { Text = "Solo immagini", Checked = true, AutoSize = true, Margin = new Padding(8, 8, 8, 0) };
    readonly CheckBox _recursive = new() { Text = "Includi sottocartelle", Checked = true, AutoSize = true, Margin = new Padding(8, 8, 8, 0) };
    readonly Button _rescan = new() { Text = "Cerca di nuovo", AutoSize = true };
    readonly Button _csv = new() { Text = "Esporta CSV", AutoSize = true, Enabled = false };
    readonly Button _stop = new() { Text = "Interrompi", AutoSize = true, Enabled = false };
    CancellationTokenSource? _cts;
    List<DupGroup> _groups = new();

    static readonly Color Warn = Color.FromArgb(255, 224, 178);   // arancio chiaro: date diverse
    static readonly Color Same = Color.FromArgb(220, 240, 220);   // verde chiaro: date uguali

    public DupesForm(string root)
    {
        _root = root;
        Text = "Smart Move - foto duplicate"; Width = 1100; Height = 650; StartPosition = FormStartPosition.CenterScreen;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4) };
        top.Controls.AddRange(new Control[] { _rescan, _stop, _csv, _images, _recursive,
            new Label { Text = "   Cartella: " + root, AutoSize = true, Margin = new Padding(8, 11, 0, 0) } });
        var legend = new Label { Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 4, 8, 0), Text = "Il confronto è SOLO sulla dimensione in byte.   ■ arancio = data di creazione (o estensione) diversa: da controllare   ■ verde = stessa data di creazione: quasi certamente doppioni" };
        foreach (var (t, w) in new[] { ("Nome", 220), ("Est.", 55), ("Dimensione", 100), ("Creato", 135), ("Modificato", 135), ("Cartella", 420) })
            _list.Columns.Add(t, w);
        Controls.Add(_list); Controls.Add(legend); Controls.Add(top); Controls.Add(_status);
        _rescan.Click += async (_, _) => await Scan();
        _stop.Click += (_, _) => _cts?.Cancel();
        _csv.Click += (_, _) => Export();
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) OpenSelected(); };
        Shown += async (_, _) => await Scan();
        FormClosing += (_, _) => _cts?.Cancel();
    }

    async Task Scan()
    {
        _cts?.Cancel(); _cts = new CancellationTokenSource(); var ct = _cts.Token;
        _list.Items.Clear(); _list.Groups.Clear(); _csv.Enabled = false; _stop.Enabled = true; _rescan.Enabled = false;
        var prog = new Progress<int>(n => _status.Text = $"Scansione... {n} file esaminati");
        try
        {
            bool imgs = _images.Checked, rec = _recursive.Checked;
            _groups = await Task.Run(() => DuplicateFinder.Find(_root, rec, imgs ? DuplicateFinder.ImageExtensions : null, 2.0, prog, ct), ct);
            Fill();
        }
        catch (OperationCanceledException) { _status.Text = "Interrotto."; }
        catch (Exception ex) { _status.Text = "Errore: " + ex.Message; }
        _stop.Enabled = false; _rescan.Enabled = true;
    }

    void Fill()
    {
        _list.BeginUpdate();
        int gi = 0;
        foreach (var g in _groups)
        {
            gi++;
            var note = g.CreatedDiffer ? "⚠ date di creazione DIVERSE" : "date di creazione uguali";
            if (g.ExtensionsDiffer) note += " · ⚠ estensioni diverse";
            var grp = new ListViewGroup($"#{gi}  -  {g.Files.Count} file da {Fmt(g.Size)}  -  recuperabili {Fmt(g.WastedBytes)}  -  {note}");
            _list.Groups.Add(grp);
            foreach (var f in g.Files)
            {
                var it = new ListViewItem(f.Name, grp) { UseItemStyleForSubItems = false, Tag = f };
                it.SubItems.Add(f.Extension); it.SubItems.Add(Fmt(f.Size));
                it.SubItems.Add(f.Created.ToString("g")); it.SubItems.Add(f.Modified.ToString("g"));
                it.SubItems.Add(Path.GetDirectoryName(f.Path) ?? "");
                var created = it.SubItems[3]; created.BackColor = g.CreatedDiffer ? Warn : Same;
                if (g.ExtensionsDiffer) it.SubItems[1].BackColor = Warn;
                _list.Items.Add(it);
            }
        }
        _list.EndUpdate();
        long wasted = _groups.Sum(g => g.WastedBytes); int diff = _groups.Count(g => g.CreatedDiffer);
        _status.Text = _groups.Count == 0 ? "Nessun file con la stessa dimensione trovato."
            : $"{_groups.Count} gruppi, {_groups.Sum(g => g.Files.Count)} file, spazio recuperabile {Fmt(wasted)}.  Gruppi con date di creazione diverse: {diff}.  Doppio clic = mostra nella cartella.";
        _csv.Enabled = _groups.Count > 0;
    }

    void OpenSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not FileEntry f) return;
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{f.Path}\""); } catch { }
    }

    void Export()
    {
        using var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "foto_duplicate.csv" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var sb = new StringBuilder("Gruppo;Nome;Estensione;Byte;Creato;Modificato;Percorso;DateDiverse;EstensioniDiverse\r\n");
        int gi = 0;
        foreach (var g in _groups) { gi++; foreach (var f in g.Files) sb.AppendLine($"{gi};\"{f.Name}\";{f.Extension};{f.Size};{f.Created:s};{f.Modified:s};\"{f.Path}\";{(g.CreatedDiffer ? "si" : "no")};{(g.ExtensionsDiffer ? "si" : "no")}"); }
        File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
    }

    static string Fmt(long b) => b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024.0:F1} KB" : b < 1073741824 ? $"{b / 1048576.0:F1} MB" : $"{b / 1073741824.0:F2} GB";
}
