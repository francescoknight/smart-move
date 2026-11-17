using System.Text;
using SmartMove.Core;

namespace SmartMove.App;

/// <summary>Riepilogo dei file con la stessa dimensione in byte, a blocchi, con selezione ed eliminazione (Cestino).</summary>
sealed class DupesForm : Form
{
    readonly string _root;
    readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, ShowGroups = true, HideSelection = false, CheckBoxes = true, MultiSelect = true };
    readonly ToolStrip _bar1 = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    readonly ToolStrip _bar2 = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(8, 6, 8, 0) };
    readonly Label _sel = new() { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(8, 4, 8, 0), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
    readonly ToolStripButton _images = new("Solo immagini") { CheckOnClick = true, Checked = true };
    readonly ToolStripButton _recursive = new("Sottocartelle") { CheckOnClick = true, Checked = true };
    readonly ToolStripComboBox _sort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    readonly ToolStripButton _rescan = new("Cerca di nuovo");
    readonly ToolStripButton _stop = new("Interrompi") { Enabled = false };
    readonly ToolStripButton _csv = new("Esporta CSV") { Enabled = false };
    readonly ToolStripButton _delSafe = new("Elimina duplicati SICURI (dimensione + data)") { BackColor = Color.FromArgb(200, 230, 200) };
    readonly ToolStripButton _delAll = new("Elimina TUTTI i duplicati (solo dimensione)") { BackColor = Color.FromArgb(255, 205, 190) };
    readonly ToolStripButton _delSel = new("Elimina spuntati");
    CancellationTokenSource? _cts;
    List<DupGroup> _groups = new();
    Dictionary<string, string> _status0 = new();
    bool _busy;   // sopprime ItemChecked durante gli aggiornamenti in blocco

    static readonly Color Warn = Color.FromArgb(255, 224, 178);   // arancio: data/estensione diversa
    static readonly Color Same = Color.FromArgb(220, 240, 220);   // verde: stessa data
    static readonly (string Label, DupSort Key)[] Sorts =
    {
        ("Spazio recuperabile (più grandi prima)", DupSort.WastedSpace), ("Dimensione del file", DupSort.FileSize),
        ("Numero di copie", DupSort.CopyCount), ("Nome", DupSort.Name)
    };

    public DupesForm(string root)
    {
        _root = root;
        Text = "Smart Move - foto duplicate"; Width = 1250; Height = 760; StartPosition = FormStartPosition.CenterScreen;
        foreach (var s in Sorts) _sort.Items.Add(s.Label);
        _sort.SelectedIndex = 0;

        var selMenu = new ToolStripDropDownButton("Seleziona");
        selMenu.DropDownItems.Add("Duplicati sicuri (stessa dimensione e data)", null, (_, _) => SelectPaths(AllSafe()));
        selMenu.DropDownItems.Add("Tutti i duplicati (tieni 1 copia per blocco)", null, (_, _) => SelectPaths(AllDup()));
        selMenu.DropDownItems.Add(new ToolStripSeparator());
        selMenu.DropDownItems.Add("Tutto", null, (_, _) => SetAll(true));
        selMenu.DropDownItems.Add("Niente", null, (_, _) => SetAll(false));
        selMenu.DropDownItems.Add("Inverti", null, (_, _) => Invert());
        var groupsMenu = new ToolStripDropDownButton("Blocchi");
        groupsMenu.DropDownItems.Add("Comprimi tutti", null, (_, _) => Collapse(true));
        groupsMenu.DropDownItems.Add("Espandi tutti", null, (_, _) => Collapse(false));

        _bar1.Items.AddRange(new ToolStripItem[] { _rescan, _stop, new ToolStripSeparator(), _images, _recursive, new ToolStripSeparator(),
            new ToolStripLabel("Ordina i blocchi per:"), _sort, new ToolStripSeparator(), groupsMenu, _csv });
        _bar2.Items.AddRange(new ToolStripItem[] { selMenu, new ToolStripSeparator(), _delSel, new ToolStripSeparator(), _delSafe, _delAll });

        var legend = new Label
        {
            Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 4, 8, 0),
            Text = "Cartella: " + root + "\nConfronto SOLO sulla dimensione in byte.   ■ arancio = data di creazione/estensione diversa (da controllare)   ■ verde = stessa data di creazione (doppione sicuro)"
        };
        foreach (var (t, w) in new[] { ("Nome", 250), ("Est.", 50), ("Dimensione", 90), ("Creato", 130), ("Modificato", 130), ("Stato", 190), ("Cartella", 380) })
            _list.Columns.Add(t, w);
        Controls.Add(_list); Controls.Add(legend); Controls.Add(_bar2); Controls.Add(_bar1); Controls.Add(_sel); Controls.Add(_status);

        var ctx = new ContextMenuStrip();
        ctx.Items.Add("Spunta le righe selezionate", null, (_, _) => CheckSelected(true));
        ctx.Items.Add("Togli la spunta alle righe selezionate", null, (_, _) => CheckSelected(false));
        ctx.Items.Add("Spunta tutto il blocco tranne la copia consigliata", null, (_, _) => CheckBlockExceptKeeper());
        ctx.Items.Add(new ToolStripSeparator());
        ctx.Items.Add("Mostra nella cartella", null, (_, _) => OpenSelected());
        ctx.Items.Add("Apri il file", null, (_, _) => OpenFile());
        _list.ContextMenuStrip = ctx;

        _rescan.Click += async (_, _) => await Scan();
        _stop.Click += (_, _) => _cts?.Cancel();
        _csv.Click += (_, _) => Export();
        _sort.SelectedIndexChanged += (_, _) => { if (_groups.Count > 0) Fill(); };
        _images.CheckedChanged += (_, _) => { };
        _list.ItemChecked += (_, _) => { if (!_busy) UpdateSel(); };
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) OpenSelected();
            else if (e.KeyCode == Keys.Space) { CheckSelected(_list.SelectedItems.Cast<ListViewItem>().Any(i => !i.Checked)); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem i in _list.Items) i.Selected = true; }
        };
        _delSel.Click += async (_, _) => await DeleteChecked("i file spuntati");
        _delSafe.Click += async (_, _) => { SelectPaths(AllSafe()); await DeleteChecked("i duplicati SICURI (stessa dimensione e stessa data di creazione)"); };
        _delAll.Click += async (_, _) => { SelectPaths(AllDup()); await DeleteChecked("TUTTI i duplicati (anche con data di creazione diversa)"); };
        Shown += async (_, _) => await Scan();
        FormClosing += (_, _) => _cts?.Cancel();
    }

    // ---------- scansione ----------
    async Task Scan()
    {
        _cts?.Cancel(); _cts = new CancellationTokenSource(); var ct = _cts.Token;
        _list.Items.Clear(); _list.Groups.Clear(); _csv.Enabled = false; _stop.Enabled = true; _rescan.Enabled = false; SetDelete(false);
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

    SortKey CurrentSort() => new(Sorts[Math.Max(0, _sort.SelectedIndex)].Key);
    readonly record struct SortKey(DupSort Key);

    void Fill()
    {
        var checkedPaths = CheckedPaths();
        _busy = true; _list.BeginUpdate();
        _list.Items.Clear(); _list.Groups.Clear();
        _groups = DuplicateSelector.Sort(_groups, CurrentSort().Key);
        int gi = 0;
        foreach (var g in _groups)
        {
            gi++;
            var clusters = DuplicateSelector.SafeClusters(g);
            var stato = new Dictionary<string, string>();
            foreach (var f in g.Files) stato[f.Path] = "solo stessa dimensione";
            foreach (var c in clusters) foreach (var f in c.Files) stato[f.Path] = f == c.Keeper ? "★ originale consigliato" : "doppione sicuro";
            var note = g.CreatedDiffer ? "⚠ date di creazione diverse" : "stessa data di creazione";
            if (g.ExtensionsDiffer) note += " · ⚠ estensioni diverse";
            var grp = new ListViewGroup($"BLOCCO {gi}  —  {g.Files.Count} copie × {Fmt(g.Size)}  —  recuperabili {Fmt(g.WastedBytes)}  —  {note}");
            _list.Groups.Add(grp);
            try { grp.CollapsedState = ListViewGroupCollapsedState.Expanded; } catch { }
            foreach (var f in g.Files)
            {
                var it = new ListViewItem(f.Name, grp) { UseItemStyleForSubItems = false, Tag = (f, g), Checked = checkedPaths.Contains(f.Path) };
                it.SubItems.Add(f.Extension); it.SubItems.Add(Fmt(f.Size));
                it.SubItems.Add(f.Created.ToString("g")); it.SubItems.Add(f.Modified.ToString("g"));
                it.SubItems.Add(stato[f.Path]); it.SubItems.Add(Path.GetDirectoryName(f.Path) ?? "");
                it.SubItems[3].BackColor = g.CreatedDiffer ? Warn : Same;
                if (g.ExtensionsDiffer) it.SubItems[1].BackColor = Warn;
                if (stato[f.Path] == "doppione sicuro") it.SubItems[5].BackColor = Same;
                else if (stato[f.Path].StartsWith("★")) it.SubItems[5].Font = new Font(_list.Font, FontStyle.Bold);
                _list.Items.Add(it);
            }
        }
        _list.EndUpdate(); _busy = false;
        long wasted = _groups.Sum(g => g.WastedBytes); int safe = AllSafe().Count;
        _status.Text = _groups.Count == 0 ? "Nessun file con la stessa dimensione trovato."
            : $"{_groups.Count} blocchi, {_groups.Sum(g => g.Files.Count)} file, recuperabili {Fmt(wasted)}.   Doppioni sicuri (stessa data): {safe}.   Doppio clic = mostra nella cartella.";
        _csv.Enabled = _groups.Count > 0; SetDelete(_groups.Count > 0); UpdateSel();
    }

    void SetDelete(bool on) { _delSafe.Enabled = _delAll.Enabled = _delSel.Enabled = on; }

    // ---------- selezione ----------
    List<FileEntry> AllSafe() => _groups.SelectMany(g => DuplicateSelector.SafeDeletes(g)).ToList();
    List<FileEntry> AllDup() => _groups.SelectMany(g => DuplicateSelector.AllDeletes(g)).ToList();

    HashSet<string> CheckedPaths() =>
        _list.CheckedItems.Cast<ListViewItem>().Select(i => ((FileEntry)((ValueTuple<FileEntry, DupGroup>)i.Tag!).Item1).Path).ToHashSet();

    void SelectPaths(IEnumerable<FileEntry> files)
    {
        var set = files.Select(f => f.Path).ToHashSet();
        _busy = true; _list.BeginUpdate();
        foreach (ListViewItem i in _list.Items) i.Checked = set.Contains(((ValueTuple<FileEntry, DupGroup>)i.Tag!).Item1.Path);
        _list.EndUpdate(); _busy = false; UpdateSel();
    }
    void SetAll(bool v) { _busy = true; _list.BeginUpdate(); foreach (ListViewItem i in _list.Items) i.Checked = v; _list.EndUpdate(); _busy = false; UpdateSel(); }
    void Invert() { _busy = true; _list.BeginUpdate(); foreach (ListViewItem i in _list.Items) i.Checked = !i.Checked; _list.EndUpdate(); _busy = false; UpdateSel(); }
    void CheckSelected(bool v) { _busy = true; foreach (ListViewItem i in _list.SelectedItems) i.Checked = v; _busy = false; UpdateSel(); }
    void CheckBlockExceptKeeper()
    {
        if (_list.SelectedItems.Count == 0) return;
        var (_, g) = ((FileEntry, DupGroup))_list.SelectedItems[0].Tag!;
        var keep = DuplicateSelector.PickKeeper(g.Files);
        _busy = true;
        foreach (ListViewItem i in _list.Items) { var (f, gg) = ((FileEntry, DupGroup))i.Tag!; if (gg == g) i.Checked = f != keep; }
        _busy = false; UpdateSel();
    }
    void Collapse(bool c) { foreach (ListViewGroup g in _list.Groups) try { g.CollapsedState = c ? ListViewGroupCollapsedState.Collapsed : ListViewGroupCollapsedState.Expanded; } catch { } }

    void UpdateSel()
    {
        var p = CheckedPaths();
        long bytes = _groups.SelectMany(g => g.Files).Where(f => p.Contains(f.Path)).Sum(f => f.Size);
        _sel.Text = p.Count == 0 ? "Nessun file spuntato" : $"Spuntati: {p.Count} file — {Fmt(bytes)} da mandare nel Cestino";
    }

    // ---------- eliminazione ----------
    async Task DeleteChecked(string what)
    {
        var selected = CheckedPaths();
        if (selected.Count == 0) { MessageBox.Show(this, "Non c'è nulla da eliminare con questa regola.", "Smart Move"); return; }
        var (del, adjusted) = DuplicateSelector.EnsureOneKept(_groups, selected);
        if (del.Count == 0) { MessageBox.Show(this, "Per sicurezza non elimino tutte le copie di un blocco: ne resta sempre almeno una.", "Smart Move"); return; }
        long bytes = del.Sum(f => f.Size);
        var msg = $"Mandare nel Cestino {del.Count} file ({Fmt(bytes)}) — {what}?\n\nIn ogni blocco resta almeno una copia. Puoi recuperarli dal Cestino." +
                  (adjusted > 0 ? $"\n\nAttenzione: in {adjusted} blocchi avevi spuntato tutte le copie; una è stata tenuta." : "");
        if (MessageBox.Show(this, msg, "Conferma eliminazione", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        SetDelete(false); _rescan.Enabled = false;
        var removed = new HashSet<string>(); var errors = new List<string>();
        await Task.Run(() =>
        {
            var deleter = new RecycleBinDeleter(); int n = 0;
            foreach (var f in del)
            {
                try { deleter.Delete(f.Path); removed.Add(f.Path); }
                catch (Exception ex) { errors.Add($"{f.Name}: {ex.Message}"); }
                if (++n % 10 == 0) BeginInvoke(() => _status.Text = $"Eliminazione... {n}/{del.Count}");
            }
        });
        _groups = DuplicateSelector.Without(_groups, removed);
        Fill(); _rescan.Enabled = true;
        _status.Text = $"Mandati nel Cestino {removed.Count} file ({Fmt(del.Where(f => removed.Contains(f.Path)).Sum(f => f.Size))}). " + (errors.Count > 0 ? $"Errori: {errors.Count}." : "");
        if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors.Take(10)), "Alcuni file non sono stati eliminati");
    }

    // ---------- varie ----------
    void OpenSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        var (f, _) = ((FileEntry, DupGroup))_list.SelectedItems[0].Tag!;
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{f.Path}\""); } catch { }
    }
    void OpenFile()
    {
        if (_list.SelectedItems.Count == 0) return;
        var (f, _) = ((FileEntry, DupGroup))_list.SelectedItems[0].Tag!;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(f.Path) { UseShellExecute = true }); } catch { }
    }

    void Export()
    {
        using var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "foto_duplicate.csv" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var sb = new StringBuilder("Blocco;Nome;Estensione;Byte;Creato;Modificato;Percorso;DateDiverse;EstensioniDiverse\r\n");
        int gi = 0;
        foreach (var g in _groups) { gi++; foreach (var f in g.Files) sb.AppendLine($"{gi};\"{f.Name}\";{f.Extension};{f.Size};{f.Created:s};{f.Modified:s};\"{f.Path}\";{(g.CreatedDiffer ? "si" : "no")};{(g.ExtensionsDiffer ? "si" : "no")}"); }
        File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
    }

    static string Fmt(long b) => b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024.0:F1} KB" : b < 1073741824 ? $"{b / 1048576.0:F1} MB" : $"{b / 1073741824.0:F2} GB";
}
