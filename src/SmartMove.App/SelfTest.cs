using SmartMove.Core;

namespace SmartMove.App;

/// <summary>SmartMove.exe --selftest &lt;srcRoot&gt; &lt;dstRoot&gt; : prova non interattiva del motore reale (CopyFileEx, Cestino). Esito in selftest.log + codice di uscita.</summary>
static class SelfTest
{
    sealed class AutoSmart : IConflictPrompt { public ConflictChoice Ask(FileInfo s, FileInfo d) => new(ConflictAction.SmartMove, true); }

    public static int Run(string srcRoot, string dstRoot)
    {
        var log = new List<string>(); int fail = 0;
        void Check(string name, bool ok) { log.Add((ok ? "OK   " : "FAIL ") + name); if (!ok) fail++; }
        try
        {
            var t = new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);
            string src = Path.Combine(srcRoot, "A", "foto"), dst = Path.Combine(dstRoot, "B", "foto");
            Directory.CreateDirectory(src); Directory.CreateDirectory(dst); Directory.CreateDirectory(Path.Combine(src, "sub"));
            void W(string dir, string n, string c) { var p = Path.Combine(dir, n); File.WriteAllText(p, c); File.SetCreationTimeUtc(p, t); File.SetLastWriteTimeUtc(p, t); }
            W(src, "uguale.jpg", "abc"); W(dst, "uguale.jpg", "abc");
            W(src, "diverso.jpg", "12345"); W(dst, "diverso.jpg", "xy");
            W(src, "nuovo.jpg", "n"); W(Path.Combine(src, "sub"), "x.txt", "x");
            var big = Path.Combine(src, "grande.bin"); File.WriteAllBytes(big, new byte[8 * 1024 * 1024]); File.SetCreationTimeUtc(big, t);

            var sameVol = Path.GetPathRoot(Path.GetFullPath(srcRoot)) == Path.GetPathRoot(Path.GetFullPath(dstRoot));
            log.Add("stesso volume: " + sameVol);
            var mover = new SmartMover(new SmartMoveOptions { VerifyAfterCopy = true }, new AutoSmart(), new RecycleBinDeleter(), new WinCopier());
            var r = mover.Run([src], Path.Combine(dstRoot, "B"), op: Operation.Move);
            foreach (var i in r.Items) log.Add($"  {i.Outcome} {Path.GetFileName(i.Source)} -> {i.Destination} {i.Error}");
            Check("nessun errore", r.Failures == 0 && r.Fatal == null);
            Check("duplicato: origine eliminata", r.Count(ItemOutcome.SourceDeletedAsDuplicate) == 1 && !File.Exists(Path.Combine(src, "uguale.jpg")));
            Check("diverso -> diverso_2.jpg", File.Exists(Path.Combine(dst, "diverso_2.jpg")) && File.ReadAllText(Path.Combine(dst, "diverso.jpg")) == "xy");
            Check("nuovo e sottocartella spostati", File.Exists(Path.Combine(dst, "nuovo.jpg")) && File.Exists(Path.Combine(dst, "sub", "x.txt")));
            Check("file grande copiato (8 MB)", new FileInfo(Path.Combine(dst, "grande.bin")).Length == 8 * 1024 * 1024);
            Check("data di creazione conservata", Math.Abs((File.GetCreationTimeUtc(Path.Combine(dst, "nuovo.jpg")) - t).TotalSeconds) < 2);
            Check("cartella sorgente rimossa", !Directory.Exists(src));

            // seconda esecuzione: stessa cosa di nuovo -> i file ora "identici" devono essere riconosciuti come duplicati
            Directory.CreateDirectory(src); W(src, "nuovo.jpg", "n");
            var r2 = mover.Run([src], Path.Combine(dstRoot, "B"), op: Operation.Move);
            Check("secondo giro: nuovo.jpg riconosciuto duplicato", r2.Count(ItemOutcome.SourceDeletedAsDuplicate) == 1);

            // copia nella stessa cartella
            var c = new SmartMover(new(), new AutoSmart(), new RecycleBinDeleter(), new WinCopier());
            c.Run([Path.Combine(dst, "nuovo.jpg")], dst, op: Operation.Copy);
            Check("copia stessa cartella: 'nuovo - Copia.jpg'", File.Exists(Path.Combine(dst, "nuovo - Copia.jpg")));
        }
        catch (Exception ex) { log.Add("ECCEZIONE " + ex); fail++; }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "selftest.log"), log);
        return fail == 0 ? 0 : 1;
    }
}
