using System.Text.Json;
using System.Text.Json.Serialization;
using SmartMove.Core;

namespace SmartMove.App;

static class Program
{
    // SmartMove.exe --tray                              residente: tray + Ctrl+V + coda lavori
    // SmartMove.exe --job  <elenco.txt> <dest> <copy|move>   (dal drop handler / menu tasto destro)
    // SmartMove.exe --move <elenco.txt> <dest>               (menu "Smart Move qui")
    // SmartMove.exe --paste <dest>                           (voce "Incolla (Smart Move)" nello sfondo)
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        try
        {
            if (args.Length >= 3 && args[0] == "--selftest") return SelfTest.Run(args[1], args[2]);
            if (args.Length >= 2 && args[0] == "--dupes")
            {
                var dir = args[1].Trim().Trim('"');                    // "C:\" arriva come C:\" per via dell'escape
                if (!Directory.Exists(dir)) { MessageBox.Show("Cartella non trovata:\n" + dir, "Smart Move"); return 2; }
                Application.Run(new DupesForm(Path.GetFullPath(dir)));
                return 0;
            }
            var opt = LoadOptions();
            if (args.Length >= 1 && args[0] == "--tray")
            {
                using var mutex = new Mutex(true, "SmartMove.Tray", out bool first);
                if (!first) return 0;
                Application.Run(new TrayContext(opt));
                return 0;
            }
            if (args.Length >= 5 && args[0] == "--job" && args[4] == "native") return NativeOps.Run(args[1], args[2], args[3]);
            JobRequest? job = ParseJob(args);
            if (job == null) { MessageBox.Show("Smart Move si usa da Esplora file (trascinamento / Ctrl+V).\nAvvia 'SmartMove.exe --tray' o esegui install.bat.", "Smart Move"); return 2; }
            if (JobService.TrySendToRunning(job)) return 0;     // il processo residente prende in carico il lavoro
            // nessun residente: esegui qui e chiudi a fine coda
            var ctx = new ApplicationContext(); var host = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized, Opacity = 0 };
            host.Show(); host.Hide();
            using var svc = new JobService(host, opt);
            svc.Idle += () => ctx.ExitThread();
            svc.Enqueue(job);
            Application.Run(ctx);
            return 0;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Smart Move - errore"); return 99; }
    }

    static JobRequest? ParseJob(string[] a)
    {
        if (a.Length >= 4 && a[0] == "--job") return FromList(a[1], a[2], a[3].Equals("copy", StringComparison.OrdinalIgnoreCase) ? Operation.Copy : Operation.Move);
        if (a.Length >= 3 && a[0] == "--move") return FromList(a[1], a[2], Operation.Move);
        if (a.Length >= 3 && a[0] == "--copy") return FromList(a[1], a[2], Operation.Copy);
        if (a.Length >= 2 && a[0] == "--paste") return PasteJob(a[1]);
        return null;
    }

    static JobRequest FromList(string list, string dest, Operation op)
    {
        var src = File.ReadAllLines(list).Where(l => l.Length > 0).ToList();
        try { File.Delete(list); } catch { }
        return new JobRequest { Sources = src, Dest = dest, Op = op };
    }

    /// <summary>Legge gli appunti (deve girare su un thread STA).</summary>
    public static JobRequest? PasteJob(string dest)
    {
        if (!Clipboard.ContainsFileDropList()) return null;
        var files = Clipboard.GetFileDropList().Cast<string>().ToList();
        bool cut = false;
        if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms && ms.Length >= 4)
            cut = (BitConverter.ToInt32(ms.ToArray(), 0) & 2) != 0 && (BitConverter.ToInt32(ms.ToArray(), 0) & 1) == 0;
        return new JobRequest { Sources = files, Dest = dest, Op = cut ? Operation.Move : Operation.Copy, ClearClipboardWhenDone = cut };
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartMove", "config.json");
    public static SmartMoveOptions LoadOptions()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var text = File.ReadAllText(ConfigPath);
                // vecchio file senza ConfigVersion: il confronto per data non era voluto -> riparti dai nuovi predefiniti
                if (text.Contains("\"ConfigVersion\"")) return JsonSerializer.Deserialize<SmartMoveOptions>(text, Json) ?? new();
                File.Copy(ConfigPath, ConfigPath + ".old", true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new SmartMoveOptions(), Json));
        }
        catch { }
        return new();
    }
}

/// <summary>Processo residente: icona nell'area di notifica, hook Ctrl+V, coda lavori.</summary>
sealed class TrayContext : ApplicationContext
{
    readonly NotifyIcon _icon; readonly KeyboardHook _hook; readonly JobService _svc; readonly Form _host;
    public TrayContext(SmartMoveOptions opt)
    {
        _host = new Form { ShowInTaskbar = false, Opacity = 0 }; _host.Show(); _host.Hide();
        _svc = new JobService(_host, opt); _svc.StartPipeServer();
        _hook = new KeyboardHook { Enabled = opt.InterceptPaste };
        _hook.PasteRequested += hwnd => _host.BeginInvoke(() => OnPaste(hwnd));

        var toggle = new ToolStripMenuItem("Intercetta Ctrl+V") { Checked = opt.InterceptPaste, CheckOnClick = true };
        toggle.CheckedChanged += (_, _) => { _hook.Enabled = toggle.Checked; };
        var menu = new ContextMenuStrip();
        menu.Items.Add(toggle);
        menu.Items.Add("Apri registro (log)", null, (_, _) => { if (File.Exists(Log.PathFile)) System.Diagnostics.Process.Start("notepad.exe", Log.PathFile); });
        menu.Items.Add("Apri configurazione", null, (_, _) => System.Diagnostics.Process.Start("notepad.exe", Program.ConfigPath));
        menu.Items.Add("Cerca aggiornamenti", null, (_, _) =>
        {
            var bat = Path.Combine(AppContext.BaseDirectory, "update.bat");
            if (File.Exists(bat)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(bat) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
            else MessageBox.Show("update.bat non trovato nella cartella del programma.", "Smart Move");
        });
        menu.Items.Add("Esci", null, (_, _) => { _icon!.Visible = false; ExitThread(); });
        _icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "Smart Move", Visible = true, ContextMenuStrip = menu };
        _icon.ShowBalloonTip(3000, "Smart Move attivo", "Ctrl+V in Esplora file è gestito da Smart Move.", ToolTipIcon.Info);
        Log.Write("Smart Move residente avviato");
    }

    void OnPaste(IntPtr hwnd)
    {
        string? dest = null; JobRequest? job = null;
        try { dest = ShellFolders.ForWindow(hwnd); if (dest != null) job = Program.PasteJob(dest); }
        catch (Exception ex) { Log.Write("Errore nel trovare la cartella: " + ex.Message); }
        if (job != null && job.Op != Operation.Move) { Log.Write("Ctrl+V: non è un taglia -> passo a Esplora"); job = null; }
        if (job == null) { Log.Write($"Ctrl+V: cartella={(dest ?? "non trovata")} -> passo a Esplora"); KeyboardHook.PassThroughPaste(); return; }   // non sappiamo gestirlo: ci pensa Esplora
        Log.Write($"Ctrl+V: {job.Op} di {job.Sources.Count} elementi in {dest}");
        _svc.Enqueue(job);
    }

    protected override void ExitThreadCore() { _hook.Dispose(); _svc.Dispose(); _icon.Dispose(); base.ExitThreadCore(); }
}
