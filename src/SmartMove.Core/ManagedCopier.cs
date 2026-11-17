namespace SmartMove.Core;

/// <summary>Copiatore gestito (flusso con buffer da 1 MB). Usato nei test e come ripiego.</summary>
public sealed class ManagedCopier : IFileCopier
{
    public void Copy(string src, string dst, bool overwrite, Action<long> onBytes, JobControl ctl)
    {
        if (!overwrite && File.Exists(dst)) throw new IOException("Il file esiste già");
        var buf = new byte[1 << 20];
        try
        {
            using (var i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
            using (var o = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                long done = 0; int n;
                while ((n = i.Read(buf, 0, buf.Length)) > 0)
                {
                    ctl.Checkpoint();
                    o.Write(buf, 0, n); done += n; onBytes(done);
                }
            }
        }
        catch (OperationCanceledException) { try { File.Delete(dst); } catch { } throw; }
    }
}
