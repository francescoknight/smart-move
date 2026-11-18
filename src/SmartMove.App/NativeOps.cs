using System.Runtime.InteropServices;

namespace SmartMove.App;

/// <summary>
/// Esegue copia/spostamento/collegamento con il motore di Windows (SHFileOperation): stesse finestre di Esplora.
/// Usato per i trascinamenti che NON sono "Maiusc+trascinamento" (Smart Move non interviene).
/// </summary>
static class NativeOps
{
    const uint FO_MOVE = 1, FO_COPY = 2;
    const ushort FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_SILENT = 0x4, FOF_NOCONFIRMMKDIR = 0x200, FOF_NOERRORUI = 0x400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string? lpszProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);

    public static int Run(string listFile, string dest, string op)
    {
        var src = File.ReadAllLines(listFile).Where(l => l.Length > 0).ToList();
        try { File.Delete(listFile); } catch { }
        dest = dest.Trim().Trim('"');
        bool ci = Environment.GetEnvironmentVariable("SMARTMOVE_CI") == "1";   // test automatici: niente finestre
        try
        {
            if (op == "link") { foreach (var s in src) MakeLink(s, dest); return 0; }
            var o = new SHFILEOPSTRUCT
            {
                wFunc = op == "move" ? FO_MOVE : FO_COPY,
                pFrom = string.Join("\0", src) + "\0\0", pTo = dest + "\0\0",
                fFlags = (ushort)(FOF_ALLOWUNDO | (ci ? FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOCONFIRMMKDIR | FOF_NOERRORUI : 0))
            };
            int r = SHFileOperationW(ref o);
            Log.Write($"native {op}: {src.Count} elementi -> {dest}, esito {r}");
            return r;
        }
        catch (Exception ex) { Log.Write("native errore: " + ex.Message); MessageBox.Show(ex.Message, "Smart Move"); return 99; }
    }

    static void MakeLink(string target, string destDir)
    {
        var name = Path.GetFileNameWithoutExtension(target.TrimEnd('\\'));
        var lnk = Path.Combine(destDir, name + " - Collegamento.lnk");
        for (int i = 2; File.Exists(lnk); i++) lnk = Path.Combine(destDir, $"{name} - Collegamento ({i}).lnk");
        dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic l = sh.CreateShortcut(lnk); l.TargetPath = target; l.Save();
    }
}
