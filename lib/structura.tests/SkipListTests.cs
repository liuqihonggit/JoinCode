namespace Structura.Collections;

public class SkipListTests {

    [Fact]
    public void InsertAndTryGetValue_单条目() {
        var sl = new SkipList<string, int>();
        sl.Insert("alpha", 1);
        Assert.True(sl.TryGetValue("alpha", out var v));
        Assert.Equal(1, v);
        Assert.Equal(1, sl.Count);
    }

    [Fact]
    public void Insert_重复key更新value() {
        var sl = new SkipList<string, int>();
        sl.Insert("k", 1);
        sl.Insert("k", 2);
        Assert.True(sl.TryGetValue("k", out var v));
        Assert.Equal(2, v);
        Assert.Equal(1, sl.Count);
    }

    [Fact]
    public void TryGetValue_不存在返回false() {
        var sl = new SkipList<string, int>();
        sl.Insert("a", 1);
        Assert.False(sl.TryGetValue("b", out _));
    }

    [Fact]
    public void Insert_多条目有序存储() {
        var sl = new SkipList<string, int>();
        sl.Insert("charlie", 3);
        sl.Insert("alpha", 1);
        sl.Insert("bravo", 2);
        var keys = sl.Enumerate().Select(x => x.Key).ToList();
        Assert.Equal(["alpha", "bravo", "charlie"], keys);
    }

    [Fact]
    public void Delete_存在返回true() {
        var sl = new SkipList<string, int>();
        sl.Insert("a", 1);
        sl.Insert("b", 2);
        Assert.True(sl.Delete("a"));
        Assert.False(sl.TryGetValue("a", out _));
        Assert.Equal(1, sl.Count);
    }

    [Fact]
    public void Delete_不存在返回false() {
        var sl = new SkipList<string, int>();
        sl.Insert("a", 1);
        Assert.False(sl.Delete("b"));
    }

    [Fact]
    public void Range_区间查询() {
        var sl = new SkipList<int, string>();
        for (var i = 0; i < 100; i++) sl.Insert(i, $"v{i}");
        var range = sl.Range(10, 15).ToList();
        Assert.Equal(5, range.Count);
        Assert.Equal(10, range[0].Key);
        Assert.Equal(14, range[^1].Key);
    }

    [Fact]
    public void Range_无上限查询() {
        var sl = new SkipList<int, string>();
        sl.Insert(1, "a");
        sl.Insert(2, "b");
        sl.Insert(3, "c");
        var range = sl.RangeFrom(2).ToList();
        Assert.Equal(2, range.Count);
        Assert.Equal("b", range[0].Value);
        Assert.Equal("c", range[1].Value);
    }

    [Fact]
    public void PrefixRange_前缀查询() {
        var sl = new SkipList<string, int>();
        sl.Insert("file/a.cs", 1);
        sl.Insert("file/b.cs", 2);
        sl.Insert("file/c.cs", 3);
        sl.Insert("other/d.cs", 4);
        var prefix = sl.PrefixRange("file/").ToList();
        Assert.Equal(3, prefix.Count);
    }

    [Fact]
    public void Insert_大量数据() {
        var sl = new SkipList<int, int>();
        const int N = 10000;
        for (var i = 0; i < N; i++) sl.Insert(i, i * 2);
        Assert.Equal(N, sl.Count);
        for (var i = 0; i < N; i++) {
            Assert.True(sl.TryGetValue(i, out var v));
            Assert.Equal(i * 2, v);
        }
    }

    [Fact]
    public void Delete_大量数据() {
        var sl = new SkipList<int, int>();
        const int N = 1000;
        for (var i = 0; i < N; i++) sl.Insert(i, i);
        for (var i = 0; i < N; i += 2) Assert.True(sl.Delete(i));
        Assert.Equal(N / 2, sl.Count);
        for (var i = 0; i < N; i++) {
            var found = sl.TryGetValue(i, out _);
            Assert.Equal(i % 2 == 1, found);
        }
    }

    [Fact]
    public void Enumerate_有序遍历() {
        var sl = new SkipList<int, int>();
        var rng = new Random(42);
        var inserted = new HashSet<int>();
        for (var i = 0; i < 500; i++) {
            var k = rng.Next(10000);
            sl.Insert(k, k);
            inserted.Add(k);
        }
        var enumerated = sl.Enumerate().Select(x => x.Key).ToList();
        Assert.Equal(inserted.Count, enumerated.Count);
        for (var i = 1; i < enumerated.Count; i++) {
            Assert.True(enumerated[i - 1] < enumerated[i]);
        }
    }
}
