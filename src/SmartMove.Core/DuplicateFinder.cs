namespace SmartMove.Core;

public sealed record FileEntry(string Path, string Name, string Extension, long Size, DateTime Created, DateTime Modified);

public sealed class DupGroup
{
    public long Size { get; init; }
    public List<FileEntry> Files { get; init; } = new();
    /// <summary>Le date di creazione non coincidono (oltre la tolleranza).</summary>
    public bool CreatedDiffer { get; init; }
    public bool ExtensionsDiffer { get; init; }
    /// <summary>Spazio recuperabile tenendo una sola copia.</summary>
    public long WastedBytes => Size * (Files.Count - 1);
}

public static class DuplicateFinder
{
    public static readonly string[] ImageExtensions =
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif",
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".raf", ".srw", ".pef"
    };

    /// <summary>Cerca file con la stessa dimensione esatta in byte. I file vuoti sono ignorati.</summary>
    /// <param name="extensions">null = tutti i file; altrimenti solo queste estensioni (con il punto).</param>
    public static List<DupGroup> Find(string root, bool recursive = true, IEnumerable<string>? extensions = null,
        double toleranceSeconds = 2.0, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var allowed = extensions == null ? null : new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = recursive, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,   // niente link simbolici/giunzioni: evita cicli
            ReturnSpecialDirectories = false
        };
        var bySize = new Dictionary<long, List<FileEntry>>();
        int n = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", opts))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length == 0) continue;
                var ext = fi.Extension;
                if (allowed != null && !allowed.Contains(ext)) continue;
                var e = new FileEntry(fi.FullName, fi.Name, ext.TrimStart('.').ToLowerInvariant(), fi.Length, fi.CreationTime, fi.LastWriteTime);
                if (!bySize.TryGetValue(e.Size, out var l)) bySize[e.Size] = l = new List<FileEntry>();
                l.Add(e);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (++n % 200 == 0) progress?.Report(n);
        }
        progress?.Report(n);

        var groups = new List<DupGroup>();
        foreach (var (size, files) in bySize)
        {
            if (files.Count < 2) continue;
            files.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
            var minC = files.Min(f => f.Created); var maxC = files.Max(f => f.Created);
            groups.Add(new DupGroup
            {
                Size = size, Files = files,
                CreatedDiffer = (maxC - minC).TotalSeconds > toleranceSeconds,
                ExtensionsDiffer = files.Select(f => f.Extension).Distinct().Count() > 1
            });
        }
        return groups.OrderByDescending(g => g.WastedBytes).ThenBy(g => g.Files[0].Path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
