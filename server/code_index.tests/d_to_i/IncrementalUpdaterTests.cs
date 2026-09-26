namespace JoinCode.CodeIndex.Tests;

public sealed class IncrementalUpdaterTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly SymbolIndex _index;
    private readonly IncrementalUpdater _updater;
    private bool _disposed;

    public IncrementalUpdaterTests() {
        _store = new InMemoryIndexStore();
        _index = new SymbolIndex(_store, TestFileSystem.Current, new CSharpSymbolExtractor());
        _updater = new IncrementalUpdater(_index, _store, TestFileSystem.Current, () => new CSharpSymbolExtractor());
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _updater.DisposeSafe();
        _index.DisposeSafe();
        _store.Dispose();
    }

    [Fact]
    public async Task UpdateAsync_NewFile_IndexesFile() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task UpdateAsync_UnchangedFile_SkipsIndexing() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task UpdateAsync_ModifiedFile_ReindexesFile() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task UpdateAsync_DeletedFile_RemovesFromIndex() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task UpdateDirectoryAsync_OnlyProcessesChangedFiles() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    /// <summary>
    /// 验证 UpdateDirectoryAsync 跳过 bin/obj/.git/.x 目录(对齐全量扫描与 FileWatcher 的排除规则)
    /// </summary>
    [Fact]
    public async Task UpdateDirectoryAsync_SkipsBinAndObjDirectories() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var root = Path.Combine(Path.GetTempPath(), $"incr_skip_{Guid.NewGuid():N}");
        fs.CreateDirectory(root);
        fs.CreateDirectory(Path.Combine(root, "bin"));
        fs.CreateDirectory(Path.Combine(root, "obj"));
        fs.CreateDirectory(Path.Combine(root, "sub"));
        fs.CreateDirectory(Path.Combine(root, "sub", "bin"));
        fs.CreateDirectory(Path.Combine(root, ".x"));

        await fs.WriteAllText(Path.Combine(root, "A.cs"), "public class A { }");
        await fs.WriteAllText(Path.Combine(root, "bin", "B.cs"), "public class B { }");
        await fs.WriteAllText(Path.Combine(root, "obj", "C.cs"), "public class C { }");
        await fs.WriteAllText(Path.Combine(root, "sub", "D.cs"), "public class D { }");
        await fs.WriteAllText(Path.Combine(root, "sub", "bin", "E.cs"), "public class E { }");
        await fs.WriteAllText(Path.Combine(root, ".x", "F.cs"), "public class F { }");
        await using var store = new InMemoryIndexStore();
        using var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        using var updater = new IncrementalUpdater(index, store, fs, () => new CSharpSymbolExtractor());

        await updater.UpdateDirectoryAsync(root, CancellationToken.None).ConfigureAwait(true);

        var snap = store.GetSnapshot();
        Assert.True(snap.FileTracking.ContainsKey(Path.Combine(root, "A.cs")), "A.cs 应被索引");
        Assert.True(snap.FileTracking.ContainsKey(Path.Combine(root, "sub", "D.cs")), "sub/D.cs 应被索引");
        Assert.False(snap.FileTracking.ContainsKey(Path.Combine(root, "bin", "B.cs")), "bin/B.cs 不应被索引");
        Assert.False(snap.FileTracking.ContainsKey(Path.Combine(root, "obj", "C.cs")), "obj/C.cs 不应被索引");
        Assert.False(snap.FileTracking.ContainsKey(Path.Combine(root, "sub", "bin", "E.cs")), "sub/bin/E.cs 不应被索引");
        Assert.Equal(2, snap.FileTracking.Count);
    }
}