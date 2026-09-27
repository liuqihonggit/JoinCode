namespace Structura.Tests;

public class ImmutableHamTTests {
    [Fact]
    public void Empty_CountIsZero() {
        ImmutableHamT<string, int>.Empty.Count.Should().Be(0);
        ImmutableHamT<string, int>.Empty.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Empty_TryGetReturnsFalse() {
        ImmutableHamT<string, int>.Empty.TryGetValue("a", out _).Should().BeFalse();
    }

    [Fact]
    public void Add_SingleElement() {
        var hamt = ImmutableHamT<string, int>.Empty.Add("a", 1);
        hamt.Count.Should().Be(1);
        hamt.TryGetValue("a", out var v).Should().BeTrue();
        v.Should().Be(1);
    }

    [Fact]
    public void Add_DuplicateKey_Throws() {
        var hamt = ImmutableHamT<string, int>.Empty.Add("a", 1);
        var act = () => hamt.Add("a", 2);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetItem_ReplacesExisting() {
        var hamt = ImmutableHamT<string, int>.Empty.Add("a", 1).SetItem("a", 2);
        hamt.Count.Should().Be(1);
        hamt["a"].Should().Be(2);
    }

    [Fact]
    public void Remove_ExistingKey() {
        var hamt = ImmutableHamT<string, int>.Empty.Add("a", 1).Add("b", 2);
        var removed = hamt.Remove("a");
        removed.Count.Should().Be(1);
        removed.ContainsKey("a").Should().BeFalse();
        removed["b"].Should().Be(2);
    }

    [Fact]
    public void Remove_NonExisting_ReturnsSame() {
        var hamt = ImmutableHamT<string, int>.Empty.Add("a", 1);
        var removed = hamt.Remove("z");
        removed.Should().BeSameAs(hamt);
    }

    [Fact]
    public void CrossValidate_RandomOperations() {
        var rng = new Random(42);
        var hamt = ImmutableHamT<int, int>.Empty;
        var dict = new Dictionary<int, int>();

        for (var op = 0; op < 5000; op++) {
            var key = rng.Next(200);
            switch (rng.Next(3)) {
                case 0:
                    var val = rng.Next(1000);
                    hamt = hamt.SetItem(key, val);
                    dict[key] = val;
                    break;
                case 1:
                    if (dict.ContainsKey(key)) {
                        hamt = hamt.Remove(key);
                        dict.Remove(key);
                    }
                    break;
                case 2:
                    hamt.TryGetValue(key, out var hv).Should().Be(dict.TryGetValue(key, out var dv));
                    if (dict.ContainsKey(key)) hv.Should().Be(dv);
                    break;
            }
            hamt.Count.Should().Be(dict.Count, $"op={op} key={key}");
        }
    }

    [Fact]
    public void CrossValidate_LargeDataset() {
        var hamt = ImmutableHamT<int, string>.Empty;
        var dict = new Dictionary<int, string>();

        for (var i = 0; i < 10000; i++) {
            hamt = hamt.Add(i, $"val{i}");
            dict.Add(i, $"val{i}");
        }

        hamt.Count.Should().Be(10000);

        for (var i = 0; i < 10000; i++) {
            hamt[i].Should().Be(dict[i]);
        }

        for (var i = 0; i < 5000; i++) {
            hamt = hamt.Remove(i * 2);
            dict.Remove(i * 2);
        }

        hamt.Count.Should().Be(dict.Count);
        foreach (var kv in dict) hamt[kv.Key].Should().Be(kv.Value);
    }

    [Fact]
    public void HashCollision_SameHashDifferentKeys() {
        var cmp = new ConstHashComparer();
        var hamt = ImmutableHamT<string, int>.Empty.WithComparer(cmp);
        hamt = hamt.Add("a", 1).Add("b", 2).Add("c", 3);

        hamt.Count.Should().Be(3);
        hamt["a"].Should().Be(1);
        hamt["b"].Should().Be(2);
        hamt["c"].Should().Be(3);

        hamt = hamt.Remove("b");
        hamt.Count.Should().Be(2);
        hamt.ContainsKey("b").Should().BeFalse();
        hamt["a"].Should().Be(1);
        hamt["c"].Should().Be(3);
    }

    [Fact]
    public void Enumerate_AllElements() {
        var hamt = ImmutableHamT<int, int>.Empty;
        for (var i = 0; i < 100; i++) hamt = hamt.Add(i, i * 10);

        var pairs = hamt.ToHashSet();
        pairs.Should().HaveCount(100);
        for (var i = 0; i < 100; i++) pairs.Should().Contain(new KeyValuePair<int, int>(i, i * 10));
    }

    [Fact]
    public void AddRange_BatchInsert() {
        var items = Enumerable.Range(0, 50).Select(i => new KeyValuePair<int, int>(i, i)).ToList();
        var hamt = ImmutableHamT<int, int>.Empty.AddRange(items);
        hamt.Count.Should().Be(50);
        hamt[25].Should().Be(25);
    }

    [Fact]
    public void SetItems_BatchUpdate() {
        var hamt = ImmutableHamT<int, int>.Empty.Add(1, 100).Add(2, 200);
        var items = new[] { new KeyValuePair<int, int>(1, 111), new KeyValuePair<int, int>(3, 333) };
        hamt = hamt.SetItems(items);
        hamt[1].Should().Be(111);
        hamt[2].Should().Be(200);
        hamt[3].Should().Be(333);
    }

    [Fact]
    public void Clear_RemovesAll() {
        var hamt = ImmutableHamT<int, int>.Empty.Add(1, 1).Add(2, 2).Clear();
        hamt.Count.Should().Be(0);
        hamt.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void KeysAndValues_EnumerateCorrectly() {
        var hamt = ImmutableHamT<string, int>.Empty.Add("x", 1).Add("y", 2).Add("z", 3);
        hamt.Keys.ToHashSet().Should().BeEquivalentTo(new[] { "x", "y", "z" });
        hamt.Values.ToHashSet().Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public void Remove_CollisionDegrades_ParentBitmapNodeShrinksToLeaf() {
        var cmp = new SplitHashComparer();
        var hamt = ImmutableHamT<string, int>.Empty.WithComparer(cmp);
        hamt = hamt.Add("a", 1).Add("b", 2).Add("c", 3);
        hamt = hamt.Remove("c");
        hamt = hamt.Remove("a");
        hamt.Count.Should().Be(1);
        hamt["b"].Should().Be(2);
        var root = GetRoot(hamt);
        root!.GetType().Name.Should().Be("LeafNode",
            "单子节点 BitmapNode 在子节点退化为 Leaf 后应一并退化为 Leaf，消除冗余路由层");
    }

    private static object? GetRoot(ImmutableHamT<string, int> hamt) {
        var f = typeof(ImmutableHamT<string, int>).GetField("_root",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return f!.GetValue(hamt);
    }

    private sealed class ConstHashComparer : IEqualityComparer<string> {
        public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);
        public int GetHashCode(string obj) => 42;
    }

    private sealed class SplitHashComparer : IEqualityComparer<string> {
        public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);
        public int GetHashCode(string obj) => obj == "c" ? 100 : 42;
    }
}
