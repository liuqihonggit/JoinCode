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
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var path = Path.Combine(Path.GetTempPath(), $"incr_new_{Guid.NewGuid():N}", "A.cs");
        fs.CreateDirectory(Path.GetDirectoryName(path)!);
        await fs.WriteAllText(path, "public class A { public void M() { } }");
        await using var store = new InMemoryIndexStore();
        using var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        using var updater = new IncrementalUpdater(index, store, fs, () => new CSharpSymbolExtractor());

        var result = await updater.UpdateAsync(path, CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.WasUpdated);
        var snap = store.GetSnapshot();
        Assert.True(snap.FileTracking.ContainsKey(path));
        Assert.True(snap.SymbolsByFqn.Count > 0);
    }

    [Fact]
    public async Task UpdateAsync_UnchangedFile_SkipsIndexing() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var path = Path.Combine(Path.GetTempPath(), $"incr_same_{Guid.NewGuid():N}", "A.cs");
        fs.CreateDirectory(Path.GetDirectoryName(path)!);
        await fs.WriteAllText(path, "public class A { }");
        await using var store = new InMemoryIndexStore();
        using var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        using var updater = new IncrementalUpdater(index, store, fs, () => new CSharpSymbolExtractor());

        // 第一次索引
        await updater.UpdateAsync(path, CancellationToken.None).ConfigureAwait(true);
        var firstSnap = store.GetSnapshot();
        var firstHash = firstSnap.FileTracking[path].Hash;

        // 第二次相同内容 → 跳过
        var result = await updater.UpdateAsync(path, CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.WasUpdated);
        Assert.Equal(firstHash, store.GetSnapshot().FileTracking[path].Hash);
    }

    [Fact]
    public async Task UpdateAsync_ModifiedFile_ReindexesFile() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var path = Path.Combine(Path.GetTempPath(), $"incr_mod_{Guid.NewGuid():N}", "A.cs");
        fs.CreateDirectory(Path.GetDirectoryName(path)!);
        await fs.WriteAllText(path, "public class Old { }");
        await using var store = new InMemoryIndexStore();
        using var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        using var updater = new IncrementalUpdater(index, store, fs, () => new CSharpSymbolExtractor());

        await updater.UpdateAsync(path, CancellationToken.None).ConfigureAwait(true);
        var firstCount = store.GetSnapshot().SymbolsByFqn.Count;

        // 修改文件内容
        await fs.WriteAllText(path, "public class New { public void Method() { } }");
        var result = await updater.UpdateAsync(path, CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.WasUpdated);
        var snap = store.GetSnapshot();
        // 新内容符号数应不同(新增了 Method)
        Assert.True(snap.SymbolsByFqn.Count > firstCount);
        Assert.Contains(snap.SymbolsByFqn, kvp => kvp.Key.Contains("New"));
    }

    [Fact]
    public async Task UpdateAsync_DeletedFile_RemovesFromIndex() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var path = Path.Combine(Path.GetTempPath(), $"incr_del_{Guid.NewGuid():N}", "A.cs");
        fs.CreateDirectory(Path.GetDirectoryName(path)!);
        await fs.WriteAllText(path, "public class A { }");
        await using var store = new InMemoryIndexStore();
        using var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        using var updater = new IncrementalUpdater(index, store, fs, () => new CSharpSymbolExtractor());

        // 先索引
        await updater.UpdateAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.True(store.GetSnapshot().FileTracking.ContainsKey(path));

        // 模拟删除:从 fs 移除文件(通过覆盖为不存在)
        // InMemoryFileSystem 没有直接删除,用新 fs 模拟文件不存在
        var fs2 = new IO.FileSystem.InMemoryFileSystem();
        fs2.CreateDirectory(Path.GetDirectoryName(path)!);
        using var index2 = new SymbolIndex(store, fs2, new CSharpSymbolExtractor());
        using var updater2 = new IncrementalUpdater(index2, store, fs2, () => new CSharpSymbolExtractor());

        var result = await updater2.UpdateAsync(path, CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.WasUpdated);
        Assert.False(store.GetSnapshot().FileTracking.ContainsKey(path));
    }

    [Fact]
    public async Task UpdateAsync_NonExistentUntrackedFile_DoesNothing() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        await using var store = new InMemoryIndexStore();
        using var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        using var updater = new IncrementalUpdater(index, store, fs, () => new CSharpSymbolExtractor());

        var result = await updater.UpdateAsync("nonexistent.cs", CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.WasUpdated);
        Assert.Empty(store.GetSnapshot().FileTracking);
    }

    [Fact]
    public async Task UpdateDirectoryAsync_OnlyProcessesChangedFiles() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var root = Path.Combine(Path.GetTempPath(), $"incr_changed_{Guid.NewGuid():N}");
        fs.CreateDirectory(root);
        await fs.WriteAllText(Path.Combine(root, "A.cs"), "public class A { }");
        await fs.WriteAllText(Path.Combine(root, "B.cs"), "public class B { }");
        await using var store = new InMemoryIndexStore();
        using var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        using var updater = new IncrementalUpdater(index, store, fs, () => new CSharpSymbolExtractor());

        // 第一次全量索引
        var first = await updater.UpdateDirectoryAsync(root, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, first.UpdatedCount);
        Assert.Equal(0, first.SkippedCount);

        // 第二次未变更 → 全部跳过
        var second = await updater.UpdateDirectoryAsync(root, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(0, second.UpdatedCount);
        Assert.Equal(2, second.SkippedCount);

        // 修改 A.cs → 仅 A.cs 重新索引
        await fs.WriteAllText(Path.Combine(root, "A.cs"), "public class A { public void NewMethod() { } }");
        var third = await updater.UpdateDirectoryAsync(root, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, third.UpdatedCount);
        Assert.Equal(1, third.SkippedCount);
    }

    [Fact]
    public async Task UpdateDirectoryAsync_RemovesDeletedFiles() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var root = Path.Combine(Path.GetTempPath(), $"incr_rm_{Guid.NewGuid():N}");
        fs.CreateDirectory(root);
        var aPath = Path.Combine(root, "A.cs");
        var bPath = Path.Combine(root, "B.cs");
        await fs.WriteAllText(aPath, "public class A { }");
        await fs.WriteAllText(bPath, "public class B { }");
        await using var store = new InMemoryIndexStore();
        using var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        using var updater = new IncrementalUpdater(index, store, fs, () => new CSharpSymbolExtractor());

        await updater.UpdateDirectoryAsync(root, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, store.GetSnapshot().FileTracking.Count);

        // 模拟删除 B.cs:用新 fs 只含 A.cs
        var fs2 = new IO.FileSystem.InMemoryFileSystem();
        fs2.CreateDirectory(root);
        await fs2.WriteAllText(aPath, "public class A { }");
        using var index2 = new SymbolIndex(store, fs2, new CSharpSymbolExtractor());
        using var updater2 = new IncrementalUpdater(index2, store, fs2, () => new CSharpSymbolExtractor());

        var result = await updater2.UpdateDirectoryAsync(root, CancellationToken.None).ConfigureAwait(true);

        var snap = store.GetSnapshot();
        Assert.True(snap.FileTracking.ContainsKey(aPath));
        Assert.False(snap.FileTracking.ContainsKey(bPath));
        Assert.Equal(1, result.DeletedCount);
    }

    [Fact]
    public async Task UpdateDirectoryAsync_EmptyDirectory_DoesNothing() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var root = Path.Combine(Path.GetTempPath(), $"incr_empty_{Guid.NewGuid():N}");
        fs.CreateDirectory(root);
        await using var store = new InMemoryIndexStore();
        using var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        using var updater = new IncrementalUpdater(index, store, fs, () => new CSharpSymbolExtractor());

        var result = await updater.UpdateDirectoryAsync(root, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.DeletedCount);
    }

    [Fact]
    public async Task UpdateAsync_NullFilePath_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _updater.UpdateAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task UpdateDirectoryAsync_NullDirectoryPath_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _updater.UpdateDirectoryAsync(null!, CancellationToken.None)).ConfigureAwait(true);
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