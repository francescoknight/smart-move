using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SmartMove.App;

/// <summary>Intercetta Ctrl+V in Esplora file. La callback fa solo controlli veloci e posta il lavoro altrove.</summary>
sealed class KeyboardHook : IDisposable
{
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    const int VK_V = 0x56, VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_MENU = 0x12;
    const uint CF_HDROP = 15;
    public static readonly UIntPtr Marker = (UIntPtr)0x534D5645; // "SMVE": tasti iniettati da noi, da lasciar passare

    delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct KBD { public uint vk, scan, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct GUITHREADINFO { public int cbSize, flags; public IntPtr active, focus, capture, menuOwner, moveSize, caret; public int l, t, r, b; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public KI ki; public long pad; }
    [StructLayout(LayoutKind.Sequential)] struct KI { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr mod, uint tid);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO i);
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint f);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? n);

    readonly HookProc _proc; IntPtr _h;
    public volatile bool Enabled = true;
    public event Action<IntPtr>? PasteRequested;   // chiamato su thread del pool, mai nella callback

    public KeyboardHook()
    {
        _proc = Callback;
        using var m = Process.GetCurrentProcess().MainModule!;
        _h = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(m.ModuleName), 0);
    }

    IntPtr Callback(int code, IntPtr w, IntPtr l)
    {
        try
        {
            if (code >= 0 && Enabled && (w == (IntPtr)WM_KEYDOWN) )
            {
                var k = Marshal.PtrToStructure<KBD>(l);
                if (k.vk == VK_V && k.extra != Marker
                    && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 && (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0 && (GetAsyncKeyState(VK_MENU) & 0x8000) == 0
                    && ShouldHandle(out var hwnd))
                {
                    ThreadPool.QueueUserWorkItem(_ => PasteRequested?.Invoke(hwnd));
                    return (IntPtr)1;   // inghiotte Ctrl+V: lo incolla Smart Move
                }
            }
        }
        catch { }
        return CallNextHookEx(_h, code, w, l);
    }

    static bool ShouldHandle(out IntPtr hwnd)
    {
        hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        var sb = new StringBuilder(64); GetClassName(hwnd, sb, 64);
        var cls = sb.ToString();
        if (cls != "CabinetWClass" && cls != "Progman" && cls != "WorkerW") return false;
        var tid = GetWindowThreadProcessId(hwnd, out _);
        var gi = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (GetGUIThreadInfo(tid, ref gi) && gi.focus != IntPtr.Zero)
        {
            var f = new StringBuilder(64); GetClassName(gi.focus, f, 64);
            var fc = f.ToString();
            if (fc.Contains("Edit", StringComparison.OrdinalIgnoreCase) || fc.Contains("ComboBox") || fc.Contains("Search")) return false; // rinomina, barra indirizzi
        }
        return IsClipboardFormatAvailable(CF_HDROP);
    }

    /// <summary>Rimanda a Esplora un Ctrl+V "vero" (contrassegnato, quindi ignorato dal nostro hook).</summary>
    public static void PassThroughPaste()
    {
        INPUT Mk(ushort vk, uint fl) => new() { type = 1, ki = new KI { vk = vk, flags = fl, extra = Marker } };
        var inp = new[] { Mk(VK_V, 0), Mk(VK_V, 2) };   // Ctrl è ancora premuto dall'utente
        SendInput((uint)inp.Length, inp, Marshal.SizeOf<INPUT>());
    }

    public void Dispose() { if (_h != IntPtr.Zero) UnhookWindowsHookEx(_h); _h = IntPtr.Zero; }
}

/// <summary>Trova la cartella aperta nella finestra di Esplora in primo piano (Shell.Application).</summary>
static class ShellFolders
{
    public static string? ForWindow(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        GetClassNameW(hwnd, sb, 64);
        if (sb.ToString() is "Progman" or "WorkerW") return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var t = Type.GetTypeFromProgID("Shell.Application");
        if (t == null) return null;
        dynamic? app = Activator.CreateInstance(t);
        if (app == null) return null;
        var matches = new List<string>();
        foreach (dynamic w in app.Windows())
        {
            try
            {
                if ((long)w.HWND != hwnd.ToInt64()) continue;
                string? p = w.Document?.Folder?.Self?.Path;
                if (p != null) matches.Add(p);
            }
            catch { }
        }
        // Windows 11 con schede: più cartelle per lo stesso HWND -> ambiguo, lasciamo fare a Esplora
        if (matches.Count != 1) return null;
        var path = matches[0];
        return Directory.Exists(path) ? path : null;   // scarta cartelle virtuali (Questo PC, Cestino, zip...)
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
}
