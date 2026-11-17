using System.Text.RegularExpressions;

namespace SmartMove.Core;

public enum DupSort { WastedSpace, FileSize, CopyCount, Name }

/// <summary>Un insieme di copie con la stessa dimensione E la stessa data di creazione (doppioni "sicuri").</summary>
public sealed record SafeCluster(List<FileEntry> Files, FileEntry Keeper)
{
    public IEnumerable<FileEntry> Deletable => Files.Where(f => f != Keeper);
}

public static class DuplicateSelector
{
    static readonly Regex CopyLike = new(@"(\(\d+\)|_\d+|\s-\s?cop(ia|y)(\s\(\d+\))?|\scopy(\s\d+)?|\scopia)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Quale copia tenere: evita i nomi "da copia" ((2), _2, - Copia), poi la data di creazione più vecchia, poi il percorso più corto.</summary>
    public static FileEntry PickKeeper(IEnumerable<FileEntry> files) =>
        files.OrderBy(f => CopyLike.IsMatch(Path.GetFileNameWithoutExtension(f.Name)) ? 1 : 0)
             .ThenBy(f => f.Created)
             .ThenBy(f => f.Path.Length)
             .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).First();

    /// <summary>Raggruppa i file del gruppo per data di creazione (tolleranza in secondi); restituisce solo i cluster con almeno 2 file.</summary>
    public static List<SafeCluster> SafeClusters(DupGroup g, double toleranceSeconds = 2.0)
    {
        var result = new List<SafeCluster>();
        var sorted = g.Files.OrderBy(f => f.Created).ToList();
        var cur = new List<FileEntry>();
        foreach (var f in sorted)
        {
            if (cur.Count > 0 && (f.Created - cur[0].Created).TotalSeconds > toleranceSeconds) { Flush(); }
            cur.Add(f);
        }
        Flush();
        return result;
        void Flush() { if (cur.Count >= 2) result.Add(new SafeCluster(cur, PickKeeper(cur))); cur = new List<FileEntry>(); }
    }

    /// <summary>Copie eliminabili con sicurezza: stessa dimensione e stessa data di creazione di un'altra copia che resta.</summary>
    public static List<FileEntry> SafeDeletes(DupGroup g, double toleranceSeconds = 2.0) =>
        SafeClusters(g, toleranceSeconds).SelectMany(c => c.Deletable).ToList();

    /// <summary>Tutte le copie tranne una (solo dimensione uguale).</summary>
    public static List<FileEntry> AllDeletes(DupGroup g)
    {
        var keep = PickKeeper(g.Files);
        return g.Files.Where(f => f != keep).ToList();
    }

    /// <summary>
    /// Protezione: se in un gruppo sono selezionate TUTTE le copie, ne toglie una (quella consigliata).
    /// Restituisce i file davvero eliminabili e quanti gruppi sono stati corretti.
    /// </summary>
    public static (List<FileEntry> Delete, int GroupsAdjusted) EnsureOneKept(IEnumerable<DupGroup> groups, ISet<string> selectedPaths)
    {
        var del = new List<FileEntry>(); int adjusted = 0;
        foreach (var g in groups)
        {
            var sel = g.Files.Where(f => selectedPaths.Contains(f.Path)).ToList();
            if (sel.Count == 0) continue;
            if (sel.Count == g.Files.Count) { sel.Remove(PickKeeper(g.Files)); adjusted++; }
            del.AddRange(sel);
        }
        return (del, adjusted);
    }

    public static List<DupGroup> Sort(IEnumerable<DupGroup> groups, DupSort by) => by switch
    {
        DupSort.FileSize => groups.OrderByDescending(g => g.Size).ThenBy(g => g.Files[0].Path, StringComparer.OrdinalIgnoreCase).ToList(),
        DupSort.CopyCount => groups.OrderByDescending(g => g.Files.Count).ThenByDescending(g => g.Size).ToList(),
        DupSort.Name => groups.OrderBy(g => g.Files[0].Name, StringComparer.OrdinalIgnoreCase).ToList(),
        _ => groups.OrderByDescending(g => g.WastedBytes).ThenBy(g => g.Files[0].Path, StringComparer.OrdinalIgnoreCase).ToList()
    };

    /// <summary>Rimuove file già eliminati dai gruppi; i gruppi con meno di 2 file spariscono.</summary>
    public static List<DupGroup> Without(IEnumerable<DupGroup> groups, ISet<string> removedPaths, double tol = 2.0)
    {
        var res = new List<DupGroup>();
        foreach (var g in groups)
        {
            var left = g.Files.Where(f => !removedPaths.Contains(f.Path)).ToList();
            if (left.Count < 2) continue;
            var minC = left.Min(f => f.Created); var maxC = left.Max(f => f.Created);
            res.Add(new DupGroup { Size = g.Size, Files = left, CreatedDiffer = (maxC - minC).TotalSeconds > tol, ExtensionsDiffer = left.Select(f => f.Extension).Distinct().Count() > 1 });
        }
        return res;
    }
}
