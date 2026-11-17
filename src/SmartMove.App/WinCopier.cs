using System.ComponentModel;
using System.Runtime.InteropServices;
using SmartMove.Core;

namespace SmartMove.App;

/// <summary>Copia con CopyFileEx: avanzamento, pausa/annulla, flussi alternativi, percorsi lunghi.</summary>
sealed class WinCopier : IFileCopier
{
    delegate uint CopyProgressRoutine(long total, long transferred, long streamSize, long streamTransferred,
        uint streamNumber, uint reason, IntPtr src, IntPtr dst, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CopyFileExW(string src, string dst, CopyProgressRoutine cb, IntPtr data, ref int cancel, uint flags);

    const uint COPY_FILE_FAIL_IF_EXISTS = 1, COPY_FILE_ALLOW_DECRYPTED_DESTINATION = 8;
    const uint PROGRESS_CONTINUE = 0, PROGRESS_CANCEL = 1;
    const int ERROR_REQUEST_ABORTED = 1235;

    public void Copy(string src, string dst, bool overwrite, Action<long> onBytes, JobControl ctl)
    {
        int cancel = 0; bool cancelled = false;
        CopyProgressRoutine cb = (_, done, _, _, _, _, _, _, _) =>
        {
            try { ctl.Checkpoint(); onBytes(done); return PROGRESS_CONTINUE; }
            catch (OperationCanceledException) { cancelled = true; return PROGRESS_CANCEL; }
        };
        uint flags = COPY_FILE_ALLOW_DECRYPTED_DESTINATION | (overwrite ? 0u : COPY_FILE_FAIL_IF_EXISTS);
        bool ok = CopyFileExW(Long(src), Long(dst), cb, IntPtr.Zero, ref cancel, flags);
        GC.KeepAlive(cb);
        if (ok) return;
        int err = Marshal.GetLastWin32Error();
        if (cancelled || err == ERROR_REQUEST_ABORTED) { try { File.Delete(dst); } catch { } throw new OperationCanceledException(); }
        if (err == 5) throw new UnauthorizedAccessException(new Win32Exception(err).Message);
        throw new IOException(new Win32Exception(err).Message);
    }

    static string Long(string p) =>
        p.StartsWith(@"\\?\") || p.Length < 240 ? p : p.StartsWith(@"\\") ? @"\\?\UNC\" + p[2..] : @"\\?\" + p;
}
