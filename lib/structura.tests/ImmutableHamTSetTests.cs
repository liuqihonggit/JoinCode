namespace Structura.Tests;

public class ImmutableHamTSetTests {
    [Fact]
    public void Empty_CountIsZero() {
        ImmutableHamTSet<string>.Empty.Count.Should().Be(0);
        ImmutableHamTSet<string>.Empty.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Empty_ContainsReturnsFalse() {
        ImmutableHamTSet<string>.Empty.Contains("a").Should().BeFalse();
    }

    [Fact]
    public void Add_SingleElement() {
        var set = ImmutableHamTSet<string>.Empty.Add("a");
        set.Count.Should().Be(1);
        set.Contains("a").Should().BeTrue();
    }

    [Fact]
    public void Add_Duplicate_ReturnsSameInstance() {
        var set = ImmutableHamTSet<string>.Empty.Add("a");
        var added = set.Add("a");
        added.Should().BeSameAs(set);
        added.Count.Should().Be(1);
    }

    [Fact]
    public void Add_MultipleElements() {
        var set = ImmutableHamTSet<int>.Empty.Add(1).Add(2).Add(3);
        set.Count.Should().Be(3);
        set.Contains(1).Should().BeTrue();
        set.Contains(2).Should().BeTrue();
        set.Contains(3).Should().BeTrue();
    }

    [Fact]
    public void Remove_ExistingElement() {
        var set = ImmutableHamTSet<int>.Empty.Add(1).Add(2);
        var removed = set.Remove(1);
        removed.Count.Should().Be(1);
        removed.Contains(1).Should().BeFalse();
        removed.Contains(2).Should().BeTrue();
    }

    [Fact]
    public void Remove_NonExisting_ReturnsSame() {
        var set = ImmutableHamTSet<int>.Empty.Add(1);
        var removed = set.Remove(99);
        removed.Should().BeSameAs(set);
    }

    [Fact]
    public void Clear_RemovesAll() {
        var set = ImmutableHamTSet<int>.Empty.Add(1).Add(2).Add(3).Clear();
        set.Count.Should().Be(0);
        set.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AddRange_BatchInsert() {
        var set = ImmutableHamTSet<int>.Empty.AddRange(Enumerable.Range(0, 50));
        set.Count.Should().Be(50);
        set.Contains(25).Should().BeTrue();
    }

    [Fact]
    public void RemoveRange_BatchRemove() {
        var set = ImmutableHamTSet<int>.Empty.AddRange(Enumerable.Range(0, 100));
        var removed = set.RemoveRange(Enumerable.Range(0, 50));
        removed.Count.Should().Be(50);
        removed.Contains(0).Should().BeFalse();
        removed.Contains(50).Should().BeTrue();
    }

    [Fact]
    public void Enumerate_AllElements() {
        var set = ImmutableHamTSet<int>.Empty;
        for (var i = 0; i < 100; i++) set = set.Add(i);
        var items = set.ToHashSet();
        items.Should().HaveCount(100);
        for (var i = 0; i < 100; i++) items.Should().Contain(i);
    }

    [Fact]
    public void CreateRange_FromItems() {
        var set = ImmutableHamTSet.CreateRange(new[] { "a", "b", "c" });
        set.Count.Should().Be(3);
        set.Contains("b").Should().BeTrue();
    }

    [Fact]
    public void WithComparer_CaseInsensitive() {
        var set = ImmutableHamTSet<string>.Create(StringComparer.OrdinalIgnoreCase);
        set = set.Add("Hello");
        set.Contains("hello").Should().BeTrue();
        set.Contains("HELLO").Should().BeTrue();
    }

    [Fact]
    public void HashCollision_SameHashDifferentKeys() {
        var cmp = new ConstHashComparer();
        var set = ImmutableHamTSet<string>.Create(cmp);
        set = set.Add("a").Add("b").Add("c");
        set.Count.Should().Be(3);
        set.Contains("a").Should().BeTrue();
        set.Contains("b").Should().BeTrue();
        set.Contains("c").Should().BeTrue();
        set = set.Remove("b");
        set.Count.Should().Be(2);
        set.Contains("b").Should().BeFalse();
    }

    [Fact]
    public void Builder_AddAndBuild() {
        var builder = ImmutableHamTSet.CreateBuilder<int>();
        builder.Add(1);
        builder.Add(2);
        builder.Add(3);
        builder.Add(1);
        builder.Count.Should().Be(3);
        var set = builder.ToImmutable();
        set.Count.Should().Be(3);
        set.Contains(2).Should().BeTrue();
    }

    [Fact]
    public void Builder_Remove() {
        var builder = ImmutableHamTSet.CreateBuilder<int>();
        builder.Add(1);
        builder.Add(2);
        builder.Add(3);
        builder.Remove(2).Should().BeTrue();
        builder.Count.Should().Be(2);
        builder.Contains(2).Should().BeFalse();
    }

    [Fact]
    public void Builder_SetOperations() {
        var builder = ImmutableHamTSet.CreateBuilder<int>();
        builder.Add(1);
        builder.Add(2);
        builder.Add(3);
        builder.UnionWith(new[] { 3, 4, 5 });
        builder.Count.Should().Be(5);
        builder.ExceptWith(new[] { 2, 4 });
        builder.Count.Should().Be(3);
        builder.Contains(2).Should().BeFalse();
        builder.Contains(4).Should().BeFalse();
    }

    [Fact]
    public void ToBuilder_RoundTrip() {
        var set = ImmutableHamTSet<int>.Empty.Add(1).Add(2).Add(3);
        var builder = set.ToBuilder();
        builder.Count.Should().Be(3);
        builder.Add(4);
        var result = builder.ToImmutable();
        result.Count.Should().Be(4);
        result.Contains(4).Should().BeTrue();
        set.Count.Should().Be(3);
    }

    [Fact]
    public void SetEquals_SameElements() {
        var set1 = ImmutableHamTSet<int>.Empty.Add(1).Add(2).Add(3);
        var set2 = ImmutableHamTSet<int>.Empty.Add(3).Add(2).Add(1);
        set1.SetEquals(set2).Should().BeTrue();
    }

    [Fact]
    public void SetEquals_DifferentElements() {
        var set1 = ImmutableHamTSet<int>.Empty.Add(1).Add(2);
        var set2 = ImmutableHamTSet<int>.Empty.Add(1).Add(3);
        set1.SetEquals(set2).Should().BeFalse();
    }

    [Fact]
    public void IsSubsetOf_True() {
        var subset = ImmutableHamTSet<int>.Empty.Add(1).Add(2);
        var superset = ImmutableHamTSet<int>.Empty.Add(1).Add(2).Add(3);
        subset.IsSubsetOf(superset).Should().BeTrue();
    }

    [Fact]
    public void IsSubsetOf_False() {
        var set1 = ImmutableHamTSet<int>.Empty.Add(1).Add(4);
        var set2 = ImmutableHamTSet<int>.Empty.Add(1).Add(2).Add(3);
        set1.IsSubsetOf(set2).Should().BeFalse();
    }

    [Fact]
    public void IsSupersetOf_True() {
        var superset = ImmutableHamTSet<int>.Empty.Add(1).Add(2).Add(3);
        var subset = ImmutableHamTSet<int>.Empty.Add(1).Add(2);
        superset.IsSupersetOf(subset).Should().BeTrue();
    }

    [Fact]
    public void IsProperSubsetOf_True() {
        var subset = ImmutableHamTSet<int>.Empty.Add(1).Add(2);
        var superset = ImmutableHamTSet<int>.Empty.Add(1).Add(2).Add(3);
        subset.IsProperSubsetOf(superset).Should().BeTrue();
    }

    [Fact]
    public void IsProperSubsetOf_False_EqualSets() {
        var set1 = ImmutableHamTSet<int>.Empty.Add(1).Add(2);
        var set2 = ImmutableHamTSet<int>.Empty.Add(1).Add(2);
        set1.IsProperSubsetOf(set2).Should().BeFalse();
    }

    [Fact]
    public void IsProperSupersetOf_True() {
        var superset = ImmutableHamTSet<int>.Empty.Add(1).Add(2).Add(3);
        var subset = ImmutableHamTSet<int>.Empty.Add(1).Add(2);
        superset.IsProperSupersetOf(subset).Should().BeTrue();
    }

    [Fact]
    public void Overlaps_True() {
        var set1 = ImmutableHamTSet<int>.Empty.Add(1).Add(2);
        var set2 = ImmutableHamTSet<int>.Empty.Add(2).Add(3);
        set1.Overlaps(set2).Should().BeTrue();
    }

    [Fact]
    public void Overlaps_False() {
        var set1 = ImmutableHamTSet<int>.Empty.Add(1).Add(2);
        var set2 = ImmutableHamTSet<int>.Empty.Add(3).Add(4);
        set1.Overlaps(set2).Should().BeFalse();
    }

    [Fact]
    public void ToImmutableHamTSet_Extension() {
        var set = Enumerable.Range(0, 10).ToImmutableHamTSet();
        set.Count.Should().Be(10);
        set.Contains(5).Should().BeTrue();
    }

    [Fact]
    public void ToImmutableHamTSet_WithComparer() {
        var set = new[] { "A", "a", "B" }.ToImmutableHamTSet(StringComparer.OrdinalIgnoreCase);
        set.Count.Should().Be(2);
        set.Contains("a").Should().BeTrue();
        set.Contains("b").Should().BeTrue();
    }

    [Fact]
    public void CrossValidate_RandomOperations() {
        var rng = new Random(42);
        var hamtSet = ImmutableHamTSet<int>.Empty;
        var hashSet = new HashSet<int>();

        for (var op = 0; op < 5000; op++) {
            var key = rng.Next(200);
            switch (rng.Next(3)) {
                case 0:
                    hamtSet = hamtSet.Add(key);
                    hashSet.Add(key);
                    break;
                case 1:
                    if (hashSet.Contains(key)) {
                        hamtSet = hamtSet.Remove(key);
                        hashSet.Remove(key);
                    }
                    break;
                case 2:
                    hamtSet.Contains(key).Should().Be(hashSet.Contains(key), $"op={op} key={key}");
                    break;
            }
            hamtSet.Count.Should().Be(hashSet.Count, $"op={op} key={key}");
        }
    }

    [Fact]
    public void CrossValidate_LargeDataset() {
        var hamtSet = ImmutableHamTSet<int>.Empty;
        var hashSet = new HashSet<int>();

        for (var i = 0; i < 10000; i++) {
            hamtSet = hamtSet.Add(i);
            hashSet.Add(i);
        }

        hamtSet.Count.Should().Be(10000);

        for (var i = 0; i < 10000; i++)
            hamtSet.Contains(i).Should().BeTrue();

        for (var i = 0; i < 5000; i++) {
            hamtSet = hamtSet.Remove(i * 2);
            hashSet.Remove(i * 2);
        }

        hamtSet.Count.Should().Be(hashSet.Count);
        foreach (var item in hashSet) hamtSet.Contains(item).Should().BeTrue();
    }

    [Fact]
    public void Factory_Create_WithComparer() {
        var set = ImmutableHamTSet.Create<string>(StringComparer.OrdinalIgnoreCase);
        set.Comparer.Should().BeSameAs(StringComparer.OrdinalIgnoreCase);
        set.Count.Should().Be(0);
    }

    [Fact]
    public void Factory_CreateBuilder_WithComparer() {
        var builder = ImmutableHamTSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        builder.Add("Hello");
        builder.Contains("hello").Should().BeTrue();
    }

    private sealed class ConstHashComparer : IEqualityComparer<string> {
        public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);
        public int GetHashCode(string obj) => 42;
    }
}
