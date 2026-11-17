using SmartMove.Core;
using Xunit;
using Progress = SmartMove.Core.Progress;

public class SmartMoverTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "sm_" + Guid.NewGuid().ToString("N"));
    readonly string src, dst;
    public SmartMoverTests() { src = Path.Combine(root, "src"); dst = Path.Combine(root, "dst"); Directory.CreateDirectory(src); Directory.CreateDirectory(dst); }
    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    sealed class Fixed(ConflictAction a, bool all = true) : IConflictPrompt
    { public int Calls; public ConflictChoice Ask(FileInfo s, FileInfo d) { Calls++; return new(a, all); } }

    static string W(string dir, string name, string content, DateTime? created = null)
    {
        var p = Path.Combine(dir, name); File.WriteAllText(p, content);
        if (created != null) { File.SetCreationTimeUtc(p, created.Value); File.SetLastWriteTimeUtc(p, created.Value); }
        return p;
    }
    SmartMover M(IConflictPrompt p, SmartMoveOptions? o = null) => new(o ?? new() { CompareField = TimeField.CreationTime }, p);
    static readonly DateTime T = new(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact] public void NoConflict_Moves()
    {
        var f = W(src, "a.jpg", "x"); var r = M(new Fixed(ConflictAction.SmartMove)).Run([f], dst);
        Assert.True(File.Exists(Path.Combine(dst, "a.jpg"))); Assert.False(File.Exists(f)); Assert.Equal(1, r.Count(ItemOutcome.Moved));
    }
    [Fact] public void Smart_Identical_DeletesSource()
    {
        var f = W(src, "a.jpg", "same", T); W(dst, "a.jpg", "same", T);
        var r = M(new Fixed(ConflictAction.SmartMove)).Run([f], dst);
        Assert.False(File.Exists(f)); Assert.Equal(1, r.Count(ItemOutcome.SourceDeletedAsDuplicate)); Assert.Single(Directory.GetFiles(dst));
    }
    [Fact] public void Smart_DifferentSize_RenamesProgressive()
    {
        W(dst, "a.jpg", "one", T);
        for (int i = 0; i < 3; i++) { var f = W(src, "a.jpg", "diff" + new string('x', i + 5), T); M(new Fixed(ConflictAction.SmartMove)).Run([f], dst); }
        Assert.True(File.Exists(Path.Combine(dst, "a.jpg"))); Assert.True(File.Exists(Path.Combine(dst, "a_2.jpg")));
        Assert.True(File.Exists(Path.Combine(dst, "a_3.jpg"))); Assert.True(File.Exists(Path.Combine(dst, "a_4.jpg")));
    }
    [Fact] public void Smart_DifferentCreationDate_Renames()
    {
        var f = W(src, "a.jpg", "same", T); W(dst, "a.jpg", "same", T.AddHours(1));
        var r = M(new Fixed(ConflictAction.SmartMove)).Run([f], dst);
        Assert.Equal(1, r.Count(ItemOutcome.MovedRenamed)); Assert.True(File.Exists(Path.Combine(dst, "a_2.jpg")));
    }
    [Fact] public void Smart_SizeOnly_IgnoresDates()
    {
        var f = W(src, "a.jpg", "same", T); W(dst, "a.jpg", "same", T.AddDays(30));
        var r = M(new Fixed(ConflictAction.SmartMove), new() { CompareField = TimeField.None }).Run([f], dst);
        Assert.Equal(1, r.Count(ItemOutcome.SourceDeletedAsDuplicate));
        var g = W(src, "b.jpg", "123", T); W(dst, "b.jpg", "1234", T);
        Assert.Equal(1, M(new Fixed(ConflictAction.SmartMove), new() { CompareField = TimeField.None }).Run([g], dst).Count(ItemOutcome.MovedRenamed));
    }
    [Fact] public void DefaultOptions_AreSizeOnly() => Assert.Equal(TimeField.None, new SmartMoveOptions().CompareField);
    [Fact] public void Smart_ToleranceWithin2s_IsDuplicate()
    {
        var f = W(src, "a.jpg", "same", T); W(dst, "a.jpg", "same", T.AddSeconds(1.5));
        Assert.Equal(1, M(new Fixed(ConflictAction.SmartMove)).Run([f], dst).Count(ItemOutcome.SourceDeletedAsDuplicate));
    }
    [Fact] public void ApplyToAll_AsksOnce()
    {
        var p = new Fixed(ConflictAction.SmartMove, true);
        var fs = new[] { "a", "b", "c" }.Select(n => { W(dst, n + ".txt", "1", T); return W(src, n + ".txt", "1", T); }).ToList();
        M(p).Run(fs, dst); Assert.Equal(1, p.Calls); Assert.All(fs, f => Assert.False(File.Exists(f)));
    }
    [Fact] public void NotApplyToAll_AsksEachTime()
    {
        var p = new Fixed(ConflictAction.Skip, false);
        var fs = new[] { "a", "b" }.Select(n => { W(dst, n + ".txt", "1"); return W(src, n + ".txt", "2"); }).ToList();
        M(p).Run(fs, dst); Assert.Equal(2, p.Calls); Assert.All(fs, f => Assert.True(File.Exists(f)));
    }
    [Fact] public void Overwrite_Skip_KeepBoth_Newer()
    {
        var f = W(src, "a.txt", "NEW"); W(dst, "a.txt", "old");
        M(new Fixed(ConflictAction.Overwrite)).Run([f], dst); Assert.Equal("NEW", File.ReadAllText(Path.Combine(dst, "a.txt")));
        f = W(src, "a.txt", "S"); M(new Fixed(ConflictAction.Skip)).Run([f], dst); Assert.True(File.Exists(f));
        M(new Fixed(ConflictAction.KeepBoth)).Run([f], dst); Assert.Equal("S", File.ReadAllText(Path.Combine(dst, "a (2).txt")));
        f = W(src, "b.txt", "n"); W(dst, "b.txt", "o"); File.SetLastWriteTimeUtc(f, T.AddDays(1)); File.SetLastWriteTimeUtc(Path.Combine(dst, "b.txt"), T);
        M(new Fixed(ConflictAction.OverwriteIfNewer)).Run([f], dst); Assert.Equal("n", File.ReadAllText(Path.Combine(dst, "b.txt")));
    }
    [Fact] public void Cancel_StopsAndReports()
    {
        var f = W(src, "a.txt", "1"); W(dst, "a.txt", "2");
        var r = M(new Fixed(ConflictAction.Cancel)).Run([f], dst); Assert.True(r.Cancelled); Assert.True(File.Exists(f));
    }
    [Fact] public void Folder_Merge_AppliesPerFile_RemovesEmptySource()
    {
        var sd = Directory.CreateDirectory(Path.Combine(src, "foto")).FullName; var dd = Directory.CreateDirectory(Path.Combine(dst, "foto")).FullName;
        Directory.CreateDirectory(Path.Combine(sd, "sub"));
        W(sd, "dup.jpg", "d", T); W(dd, "dup.jpg", "d", T);
        W(sd, "diff.jpg", "AAA", T); W(dd, "diff.jpg", "B", T);
        W(Path.Combine(sd, "sub"), "n.jpg", "n");
        M(new Fixed(ConflictAction.SmartMove)).Run([sd], dst);
        Assert.False(Directory.Exists(sd));
        Assert.True(File.Exists(Path.Combine(dd, "diff_2.jpg"))); Assert.True(File.Exists(Path.Combine(dd, "sub", "n.jpg")));
        Assert.Equal(3, Directory.GetFiles(dd).Length + 0 * 1 - 0 + 0); // dup, diff, diff_2
    }
    [Fact] public void SourceEqualsDestination_NoOp()
    {
        var f = W(dst, "a.txt", "1"); var p = new Fixed(ConflictAction.Overwrite);
        M(p).Run([f], dst); Assert.True(File.Exists(f)); Assert.Equal(0, p.Calls);
    }
    [Fact] public void LastWriteField_Configurable()
    {
        var f = W(src, "a.txt", "s"); var d = W(dst, "a.txt", "s");
        File.SetCreationTimeUtc(f, T); File.SetCreationTimeUtc(d, T.AddDays(5)); File.SetLastWriteTimeUtc(f, T); File.SetLastWriteTimeUtc(d, T);
        var r = M(new Fixed(ConflictAction.SmartMove), new() { CompareField = TimeField.LastWriteTime }).Run([f], dst);
        Assert.Equal(1, r.Count(ItemOutcome.SourceDeletedAsDuplicate));
    }
}

sealed class Prog(Action<Progress> a) : IProgress<Progress> { public void Report(Progress p) => a(p); }

public class V2Tests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "sm2_" + Guid.NewGuid().ToString("N"));
    readonly string src, dst;
    public V2Tests() { src = Path.Combine(root, "src"); dst = Path.Combine(root, "dst"); Directory.CreateDirectory(src); Directory.CreateDirectory(dst); }
    public void Dispose() { try { Directory.Delete(root, true); } catch { } }
    static readonly DateTime T = new(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    static string W(string dir, string n, string c, DateTime? t = null) { var p = Path.Combine(dir, n); File.WriteAllText(p, c); if (t != null) { File.SetCreationTimeUtc(p, t.Value); File.SetLastWriteTimeUtc(p, t.Value); } return p; }

    sealed class P(ConflictAction a = ConflictAction.Skip, ErrorAction e = ErrorAction.Skip, bool eAll = false, bool merge = true) : IConflictPrompt
    {
        public int Errors; public ConflictChoice Ask(FileInfo s, FileInfo d) => new(a, true);
        public ErrorChoice AskError(string p, string m) { Errors++; return new(e, eAll); }
        public bool ConfirmMerge(string s, string d) => merge;
    }
    sealed class FlakyCopier(int failures) : IFileCopier
    {
        public int Calls; readonly ManagedCopier inner = new();
        public void Copy(string s, string d, bool o, Action<long> b, JobControl c) { Calls++; if (Calls <= failures) throw new IOException("in uso"); inner.Copy(s, d, o, b, c); }
    }

    [Fact] public void Copy_KeepsSource_And_PreservesCreationTime()
    {
        var f = W(src, "a.txt", "x", T);
        var r = new SmartMover(new(), new P()).Run([f], dst, op: Operation.Copy);
        Assert.True(File.Exists(f)); Assert.True(File.Exists(Path.Combine(dst, "a.txt"))); Assert.Equal(1, r.Count(ItemOutcome.Copied));
    }
    [Fact] public void Copy_SameFolder_UsesCopiaNaming()
    {
        var f = W(src, "doc.txt", "x");
        var m = new SmartMover(new(), new P());
        m.Run([f], src, op: Operation.Copy); m.Run([f], src, op: Operation.Copy);
        Assert.True(File.Exists(Path.Combine(src, "doc - Copia.txt"))); Assert.True(File.Exists(Path.Combine(src, "doc - Copia (2).txt")));
    }
    [Fact] public void Move_SameFolder_NoOp()
    {
        var f = W(src, "doc.txt", "x"); new SmartMover(new(), new P()).Run([f], src, op: Operation.Move);
        Assert.Single(Directory.GetFiles(src));
    }
    [Fact] public void Copy_Smart_Identical_LeavesBoth()
    {
        var f = W(src, "a.txt", "s", T); W(dst, "a.txt", "s", T);
        var r = new SmartMover(new(), new P(ConflictAction.SmartMove)).Run([f], dst, op: Operation.Copy);
        Assert.True(File.Exists(f)); Assert.Single(Directory.GetFiles(dst)); Assert.Equal(1, r.Count(ItemOutcome.Skipped));
    }
    [Fact] public void Retry_ThenSucceeds()
    {
        var f = W(src, "a.txt", "x"); var fc = new FlakyCopier(2); var p = new P(e: ErrorAction.Retry);
        // forza la copia vera usando Operation.Copy
        var r = new SmartMover(new(), p, copier: fc).Run([f], dst, op: Operation.Copy);
        Assert.Equal(2, p.Errors); Assert.Equal(1, r.Count(ItemOutcome.Copied)); Assert.True(File.Exists(Path.Combine(dst, "a.txt")));
    }
    [Fact] public void Error_SkipAll_StopsAsking()
    {
        var a = W(src, "a.txt", "x"); var b = W(src, "b.txt", "y"); var fc = new FlakyCopier(99); var p = new P(e: ErrorAction.Skip, eAll: true);
        var r = new SmartMover(new(), p, copier: fc).Run([a, b], dst, op: Operation.Copy);
        Assert.Equal(1, p.Errors); Assert.Equal(2, r.Failures);
    }
    [Fact] public void Error_Cancel_Cancels()
    {
        var a = W(src, "a.txt", "x"); var r = new SmartMover(new(), new P(e: ErrorAction.Cancel), copier: new FlakyCopier(9)).Run([a], dst, op: Operation.Copy);
        Assert.True(r.Cancelled);
    }
    [Fact] public void FolderIntoItself_Refused()
    {
        var d = Directory.CreateDirectory(Path.Combine(src, "x")).FullName; var inner = Directory.CreateDirectory(Path.Combine(d, "in")).FullName;
        var r = new SmartMover(new(), new P()).Run([d], inner, op: Operation.Copy);
        Assert.NotNull(r.Fatal);
    }
    [Fact] public void Merge_Declined_Cancels()
    {
        var sd = Directory.CreateDirectory(Path.Combine(src, "f")).FullName; Directory.CreateDirectory(Path.Combine(dst, "f")); W(sd, "a", "x");
        var r = new SmartMover(new(), new P(merge: false)).Run([sd], dst, op: Operation.Copy);
        Assert.True(r.Cancelled);
    }
    [Fact] public void Pause_Resume_Cancel()
    {
        var f = W(src, "a.txt", new string('x', 100)); var ctl = new JobControl(); ctl.Pause();
        var t = Task.Run(() => new SmartMover(new(), new P()).Run([f], dst, ctl: ctl, op: Operation.Copy));
        Thread.Sleep(150); Assert.False(t.IsCompleted); ctl.Resume(); Assert.True(t.Wait(3000));
        var ctl2 = new JobControl(); ctl2.Cancel();
        Assert.True(new SmartMover(new(), new P()).Run([f], Path.Combine(root, "d2"), ctl: ctl2, op: Operation.Copy).Cancelled);
    }
    [Fact] public void ProgressReports_Bytes()
    {
        var f = W(src, "a.txt", new string('x', 5000)); Progress? last = null;
        new SmartMover(new(), new P()).Run([f], dst, new Prog(p => last = p), op: Operation.Copy);
        Thread.Sleep(100); Assert.NotNull(last); Assert.Equal(5000, last!.BytesTotal);
    }
}

public class DuplicateFinderTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "sd_" + Guid.NewGuid().ToString("N"));
    public DuplicateFinderTests() => Directory.CreateDirectory(Path.Combine(root, "sub"));
    public void Dispose() { try { Directory.Delete(root, true); } catch { } }
    string W(string rel, int size, DateTime? created = null)
    {
        var p = Path.Combine(root, rel); File.WriteAllBytes(p, new byte[size]);
        if (created != null) File.SetCreationTime(p, created.Value); return p;
    }
    static readonly DateTime T = new(2024, 5, 1, 10, 0, 0);

    [Fact] public void FindsGroupsBySizeOnly_AcrossSubfolders()
    {
        W("a.jpg", 100, T); W("sub/b.jpg", 100, T); W("c.jpg", 200); W("sub/d.png", 300);
        var g = DuplicateFinder.Find(root);
        Assert.Single(g); Assert.Equal(100, g[0].Size); Assert.Equal(2, g[0].Files.Count); Assert.Equal(100, g[0].WastedBytes);
    }
    [Fact] public void FlagsDifferentCreationDates()
    {
        W("a.jpg", 100, T); W("b.jpg", 100, T.AddDays(3));
        var g = DuplicateFinder.Find(root).Single(); Assert.True(g.CreatedDiffer);
    }
    [Fact] public void SameDates_NotFlagged()
    {
        W("a.jpg", 100, T); W("b.jpg", 100, T.AddSeconds(1));
        Assert.False(DuplicateFinder.Find(root).Single().CreatedDiffer);
    }
    [Fact] public void FlagsDifferentExtensions()
    {
        W("a.jpg", 100, T); W("b.png", 100, T);
        var g = DuplicateFinder.Find(root).Single(); Assert.True(g.ExtensionsDiffer);
    }
    [Fact] public void ImageFilter_And_NonRecursive_And_EmptyFilesIgnored()
    {
        W("a.jpg", 100, T); W("b.txt", 100, T); W("e1.jpg", 0); W("e2.jpg", 0); W("sub/c.jpg", 100, T);
        Assert.Single(DuplicateFinder.Find(root, extensions: DuplicateFinder.ImageExtensions).Single().Files.Where(f => f.Extension == "txt").DefaultIfEmpty(), x => x == null);
        Assert.Equal(2, DuplicateFinder.Find(root, extensions: DuplicateFinder.ImageExtensions).Single().Files.Count);
        Assert.Equal(3, DuplicateFinder.Find(root).Single().Files.Count);
        Assert.Equal(2, DuplicateFinder.Find(root, recursive: false).Single().Files.Count);
    }
    [Fact] public void SortedByWastedSpace()
    {
        W("a1", 10); W("a2", 10); W("b1", 1000); W("b2", 1000);
        Assert.Equal(1000, DuplicateFinder.Find(root)[0].Size);
    }
}

public class DuplicateSelectorTests
{
    static readonly DateTime T = new(2024, 5, 1, 10, 0, 0);
    static FileEntry F(string path, DateTime c, long size = 100) => new(path, Path.GetFileName(path), Path.GetExtension(path).TrimStart('.'), size, c, c);
    static DupGroup G(params FileEntry[] f) => new() { Size = f[0].Size, Files = f.ToList() };

    [Fact] public void Keeper_AvoidsCopyLikeNames()
    {
        var k = DuplicateSelector.PickKeeper(new[] { F("/a/img (2).jpg", T), F("/a/img - Copia.jpg", T), F("/a/img_2.jpg", T), F("/b/img.jpg", T) });
        Assert.Equal("/b/img.jpg", k.Path);
    }
    [Fact] public void Keeper_OldestThenShortestPath()
    {
        Assert.Equal("/x/b.jpg", DuplicateSelector.PickKeeper(new[] { F("/x/a.jpg", T.AddDays(1)), F("/x/b.jpg", T) }).Path);
        Assert.Equal("/a.jpg", DuplicateSelector.PickKeeper(new[] { F("/long/path/a.jpg", T), F("/a.jpg", T) }).Path);
    }
    [Fact] public void SafeDeletes_OnlySameCreationDate()
    {
        var g = G(F("/1/a.jpg", T), F("/2/a.jpg", T.AddSeconds(1)), F("/3/a.jpg", T.AddDays(5)));
        var d = DuplicateSelector.SafeDeletes(g);
        Assert.Single(d); Assert.DoesNotContain(d, f => f.Path == "/3/a.jpg");   // quella con data diversa NON è sicura
    }
    [Fact] public void SafeDeletes_NoneWhenAllDatesDiffer() =>
        Assert.Empty(DuplicateSelector.SafeDeletes(G(F("/1/a", T), F("/2/a", T.AddDays(1)), F("/3/a", T.AddDays(2)))));
    [Fact] public void SafeDeletes_TwoClusters_KeepsOneEach()
    {
        var g = G(F("/1/a", T), F("/2/a", T), F("/3/a", T.AddDays(9)), F("/4/a", T.AddDays(9)));
        Assert.Equal(2, DuplicateSelector.SafeDeletes(g).Count);
    }
    [Fact] public void AllDeletes_KeepsExactlyOne()
    {
        var g = G(F("/1/a", T), F("/2/a", T.AddDays(1)), F("/3/a", T.AddDays(2)), F("/4/a", T), F("/5/a", T));
        var d = DuplicateSelector.AllDeletes(g); Assert.Equal(4, d.Count);
        Assert.Single(g.Files.Except(d));
    }
    [Fact] public void EnsureOneKept_ProtectsLastCopy()
    {
        var g1 = G(F("/1/a", T), F("/2/a", T)); var g2 = G(F("/1/b", T, 50), F("/2/b", T, 50), F("/3/b", T, 50));
        var sel = new HashSet<string> { "/1/a", "/2/a", "/1/b" };
        var (del, adj) = DuplicateSelector.EnsureOneKept(new[] { g1, g2 }, sel);
        Assert.Equal(1, adj); Assert.Equal(2, del.Count);                // g1: una tenuta; g2: /1/b eliminato
        Assert.Single(del.Where(f => f.Path.EndsWith("a")));
    }
    [Fact] public void Sort_ByCountSizeWasted()
    {
        var small5 = G(Enumerable.Range(0, 5).Select(i => F($"/s{i}", T, 10)).ToArray());
        var big2 = G(F("/b1", T, 1000), F("/b2", T, 1000));
        Assert.Same(small5, DuplicateSelector.Sort(new[] { big2, small5 }, DupSort.CopyCount)[0]);
        Assert.Same(big2, DuplicateSelector.Sort(new[] { small5, big2 }, DupSort.FileSize)[0]);
        Assert.Same(big2, DuplicateSelector.Sort(new[] { small5, big2 }, DupSort.WastedSpace)[0]);   // 1000 > 40
    }
    [Fact] public void Without_DropsGroupsUnderTwo()
    {
        var g = G(F("/1/a", T), F("/2/a", T), F("/3/a", T));
        Assert.Single(DuplicateSelector.Without(new[] { g }, new HashSet<string> { "/1/a" })[0].Files.Count == 2 ? new[] { 1 } : new int[0]);
        Assert.Empty(DuplicateSelector.Without(new[] { g }, new HashSet<string> { "/1/a", "/2/a" }));
    }
}
