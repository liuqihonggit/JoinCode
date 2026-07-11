namespace CodeIndex.Tests;

public sealed class TreeCacheTests : IDisposable
{
    private readonly TreeCache _cache;
    private readonly TreeSitterParser _parser;

    public TreeCacheTests()
    {
        _cache = new TreeCache(maxEntries: 100);
        _parser = new TreeSitterParser("c-sharp");
    }

    public void Dispose()
    {
        _cache.Dispose();
        _parser.Dispose();
    }

    [Fact]
    public void TryGet_NonExistentFile_ReturnsFalse()
    {
        Assert.False(_cache.TryGet("nonexistent.cs", out _));
    }

    [Fact]
    public void Add_ThenTryGet_ReturnsSameTree()
    {
        var source = "class A { }";
        using var tree = _parser.Parse(source);

        _cache.Add("test.cs", tree, source);

        Assert.True(_cache.TryGet("test.cs", out var cached));
        Assert.NotNull(cached);
    }

    [Fact]
    public void Remove_ExistingFile_RemovesFromCache()
    {
        var source = "class A { }";
        using var tree = _parser.Parse(source);
        _cache.Add("test.cs", tree, source);

        _cache.Remove("test.cs");

        Assert.False(_cache.TryGet("test.cs", out _));
    }

    [Fact]
    public void Add_ExceedsMaxEntries_EvictsOldest()
    {
        var cache = new TreeCache(maxEntries: 3);

        try
        {
            for (var i = 0; i < 5; i++)
            {
                var source = $"class Class{i} {{ }}";
                using var tree = _parser.Parse(source);
                cache.Add($"file{i}.cs", tree, source);
            }

            Assert.False(cache.TryGet("file0.cs", out _));
            Assert.False(cache.TryGet("file1.cs", out _));
            Assert.True(cache.TryGet("file2.cs", out _));
            Assert.True(cache.TryGet("file3.cs", out _));
            Assert.True(cache.TryGet("file4.cs", out _));
        }
        finally
        {
            cache.Dispose();
        }
    }

    [Fact]
    public void GetSource_ExistingFile_ReturnsStoredSource()
    {
        var source = "class A { public void M() { } }";
        using var tree = _parser.Parse(source);
        _cache.Add("test.cs", tree, source);

        var retrieved = _cache.GetSource("test.cs");

        Assert.Equal(source, retrieved);
    }

    [Fact]
    public void GetSource_NonExistentFile_ReturnsNull()
    {
        Assert.Null(_cache.GetSource("nonexistent.cs"));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var source = "class A { }";
        using var tree = _parser.Parse(source);
        _cache.Add("file1.cs", tree, source);
        _cache.Add("file2.cs", tree, source);

        _cache.Clear();

        Assert.False(_cache.TryGet("file1.cs", out _));
        Assert.False(_cache.TryGet("file2.cs", out _));
    }

    [Fact]
    public void Count_ReflectsCurrentEntries()
    {
        Assert.Equal(0, _cache.Count);

        var source = "class A { }";
        using var tree = _parser.Parse(source);
        _cache.Add("file1.cs", tree, source);

        Assert.Equal(1, _cache.Count);

        _cache.Add("file2.cs", tree, source);

        Assert.Equal(2, _cache.Count);

        _cache.Remove("file1.cs");

        Assert.Equal(1, _cache.Count);
    }

    [Fact]
    public void Add_SameFileTwice_ReplacesOldEntry()
    {
        var source1 = "class A { }";
        var source2 = "class B { }";
        using var tree1 = _parser.Parse(source1);
        using var tree2 = _parser.Parse(source2);

        _cache.Add("test.cs", tree1, source1);
        _cache.Add("test.cs", tree2, source2);

        Assert.Equal(1, _cache.Count);
        Assert.Equal(source2, _cache.GetSource("test.cs"));
    }

    [Fact]
    public void IncrementalParse_WithCachedTree_ProducesValidTree()
    {
        var oldSource = "class A { public void M1() { } }";
        using var oldTree = _parser.Parse(oldSource);
        _cache.Add("test.cs", oldTree, oldSource);

        var newSource = "class A { public void M1() { } public void M2() { } }";
        var edit = SourceDiff.ComputeEdit(oldSource, newSource);

        Assert.True(_cache.TryGet("test.cs", out var cachedTree));
        cachedTree!.Edit(edit);

        using var newTree = _parser.Parse(newSource, cachedTree);

        Assert.NotNull(newTree);
        Assert.False(newTree.RootNode.IsError);
    }
}
