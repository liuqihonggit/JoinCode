namespace Structura.Tests;

public class SwissTableTests {
    [Fact]
    public void Empty_CountIsZero() {
        var table = new SwissTable<string, int>();
        table.Count.Should().Be(0);
    }

    [Fact]
    public void Empty_TryGetReturnsFalse() {
        var table = new SwissTable<string, int>();
        table.TryGetValue("a", out _).Should().BeFalse();
    }

    [Fact]
    public void Add_SingleElement() {
        var table = new SwissTable<string, int>();
        table.Add("a", 1);
        table.Count.Should().Be(1);
        table.TryGetValue("a", out var v).Should().BeTrue();
        v.Should().Be(1);
    }

    [Fact]
    public void Add_DuplicateKey_Throws() {
        var table = new SwissTable<string, int>();
        table.Add("a", 1);
        var act = () => table.Add("a", 2);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Add_ManyElements_AllFound() {
        var table = new SwissTable<int, string>();
        for (var i = 0; i < 1000; i++)
            table.Add(i, $"val{i}");

        table.Count.Should().Be(1000);
        for (var i = 0; i < 1000; i++) {
            table.TryGetValue(i, out var v).Should().BeTrue();
            v.Should().Be($"val{i}");
        }
    }

    [Fact]
    public void Remove_ExistingKey_ReturnsTrue() {
        var table = new SwissTable<string, int> { ["a"] = 1, ["b"] = 2 };
        table.Remove("a").Should().BeTrue();
        table.Count.Should().Be(1);
        table.ContainsKey("a").Should().BeFalse();
        table.ContainsKey("b").Should().BeTrue();
    }

    [Fact]
    public void Remove_NonExistingKey_ReturnsFalse() {
        var table = new SwissTable<string, int> { ["a"] = 1 };
        table.Remove("x").Should().BeFalse();
    }

    [Fact]
    public void Remove_And_Readd_Works() {
        var table = new SwissTable<string, int>();
        table.Add("a", 1);
        table.Remove("a");
        table.Add("a", 2);
        table.TryGetValue("a", out var v).Should().BeTrue();
        v.Should().Be(2);
    }

    [Fact]
    public void ContainsKey_ReturnsCorrectResult() {
        var table = new SwissTable<string, int> { ["a"] = 1, ["b"] = 2 };
        table.ContainsKey("a").Should().BeTrue();
        table.ContainsKey("b").Should().BeTrue();
        table.ContainsKey("c").Should().BeFalse();
    }

    [Fact]
    public void Clear_RemovesAllElements() {
        var table = new SwissTable<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
        table.Clear();
        table.Count.Should().Be(0);
        table.ContainsKey("a").Should().BeFalse();
    }

    [Fact]
    public void Indexer_Get_ThrowsWhenKeyNotFound() {
        var table = new SwissTable<string, int>();
        var act = () => _ = table["a"];
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Indexer_Set_OverwritesExisting() {
        var table = new SwissTable<string, int>();
        table["a"] = 1;
        table["a"] = 2;
        table["a"].Should().Be(2);
        table.Count.Should().Be(1);
    }

    [Fact]
    public void TryAdd_ReturnsFalseForDuplicate() {
        var table = new SwissTable<string, int>();
        table.TryAdd("a", 1).Should().BeTrue();
        table.TryAdd("a", 2).Should().BeFalse();
    }

    [Fact]
    public void Enumeration_VisitsAllElements() {
        var table = new SwissTable<int, int>();
        for (var i = 0; i < 100; i++)
            table.Add(i, i * 2);

        var keys = new HashSet<int>();
        foreach (var kvp in table)
            keys.Add(kvp.Key);

        keys.Count.Should().Be(100);
        for (var i = 0; i < 100; i++)
            keys.Should().Contain(i);
    }

    [Fact]
    public void StressTest_AddRemoveLookup() {
        var table = new SwissTable<int, int>();
        var rng = new Random(42);
        var expected = new Dictionary<int, int>();

        for (var op = 0; op < 10000; op++) {
            var key = rng.Next(0, 500);
            switch (rng.Next(0, 3)) {
                case 0:
                    table[key] = key * 3;
                    expected[key] = key * 3;
                    break;
                case 1:
                    table.Remove(key);
                    expected.Remove(key);
                    break;
                case 2:
                    table.TryGetValue(key, out var v).Should().Be(expected.TryGetValue(key, out var ev));
                    if (expected.ContainsKey(key))
                        v.Should().Be(ev);
                    break;
            }
        }

        table.Count.Should().Be(expected.Count);
    }

    [Fact]
    public void CustomComparer_Works() {
        var table = new SwissTable<string, int>(StringComparer.OrdinalIgnoreCase);
        table.Add("Hello", 1);
        table.TryGetValue("hello", out var v).Should().BeTrue();
        v.Should().Be(1);
    }

    // === P0 Bug 复现测试（红测试）===

    [Fact]
    public void Bug_Clear_ShouldInvalidateEnumerator() {
        var dict = new SwissTable<string, int>();
        dict.Add("a", 1);
        dict.Add("b", 2);

        var enumerator = dict.GetEnumerator();
        enumerator.MoveNext().Should().BeTrue();

        dict.Clear();

        var act = () => enumerator.MoveNext();
        act.Should().Throw<InvalidOperationException>("Clear 后枚举器应失效");
    }

    [Fact]
    public void Bug_Clone_ShouldHaveCorrectGrowthLeft() {
        var source = new SwissTable<int, int>();
        for (var i = 0; i < 100; i++) source.Add(i, i);

        var clone = new SwissTable<int, int>(source);

        var rawTableField = typeof(SwissTable<int, int>).GetField("rawTable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var rawTable = rawTableField.GetValue(clone)!;
        var growthLeftField = rawTable.GetType().GetField("_growth_left", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var growthLeft = (int)growthLeftField.GetValue(rawTable)!;

        var bucketMaskField = rawTable.GetType().GetField("_bucket_mask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var bucketMask = (int)bucketMaskField.GetValue(rawTable)!;
        var countField = rawTable.GetType().GetField("_count", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var count = (int)countField.GetValue(rawTable)!;

        var expectedCapacity = ((bucketMask + 1) >> 3) * 7;
        var expectedGrowthLeft = expectedCapacity - count;

        growthLeft.Should().Be(expectedGrowthLeft,
            $"_growth_left 应为 capacity({expectedCapacity}) - count({count}) = {expectedGrowthLeft}，而非 count({count})");
    }

    [Fact]
    public void Bug_FallbackGroup_Create_ShouldNotThrow() {
        var act = () => Structura.Collections.FallbackGroup.create(0x7F);
        act.Should().NotThrow("create 应返回有效组，不应抛 NotImplementedException");
    }

    [Fact]
    public void Bug_FallbackGroup_MatchGroup_ShouldNotThrow() {
        var group = Structura.Collections.FallbackGroup.create(0x7F);
        var act = () => group.MatchGroup(group);
        act.Should().NotThrow("MatchGroup 应返回匹配结果，不应抛 NotImplementedException");
    }

    // === P1 Bug 复现测试 ===

    [Fact]
    public void Bug_TrimExcess_ShouldExpandWhenCapacityLarger() {
        var dict = new SwissTable<int, int>();
        for (var i = 0; i < 10; i++) dict.Add(i, i);

        var bucketsBefore = GetBuckets(dict);
        dict.TrimExcess(1000);
        var bucketsAfter = GetBuckets(dict);

        bucketsAfter.Should().BeGreaterThan(bucketsBefore,
            "TrimExcess(1000) 应扩容以容纳 1000 个条目");
    }

    [Fact]
    public void Bug_Enumerator_DetectsAddDuringIteration() {
        var dict = new SwissTable<int, int>();
        for (var i = 0; i < 10; i++) dict.Add(i, i);

        var enumerator = dict.GetEnumerator();
        enumerator.MoveNext().Should().BeTrue();

        dict.Add(100, 100);

        var act = () => enumerator.MoveNext();
        act.Should().Throw<InvalidOperationException>("迭代中 Add 应使枚举器失效");
    }

    [Fact]
    public void Bug_LargeScale_GrowAndLookup() {
        var dict = new SwissTable<int, int>(4);
        for (var i = 0; i < 10000; i++) dict.Add(i, i * 2);

        dict.Count.Should().Be(10000);
        for (var i = 0; i < 10000; i++) {
            dict.TryGetValue(i, out var v).Should().BeTrue();
            v.Should().Be(i * 2);
        }
    }

    [Fact]
    public void Bug_Tombstone_ReuseAfterRemove() {
        var dict = new SwissTable<int, int>();
        for (var i = 0; i < 100; i++) dict.Add(i, i);
        for (var i = 0; i < 50; i++) dict.Remove(i);
        for (var i = 0; i < 50; i++) dict.Add(i, i + 1000);

        dict.Count.Should().Be(100);
        for (var i = 0; i < 50; i++) {
            dict.TryGetValue(i, out var v).Should().BeTrue();
            v.Should().Be(i + 1000, "墓碑槽应被复用");
        }
        for (var i = 50; i < 100; i++) {
            dict.TryGetValue(i, out var v).Should().BeTrue();
            v.Should().Be(i);
        }
    }

    [Fact]
    public void Bug_NullKey_ThrowsArgumentNullException() {
        var dict = new SwissTable<string, int>();
        var act = () => dict.Add(null!, 1);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Bug_FullCapacity_InsertTriggersGrow() {
        var dict = new SwissTable<int, int>(4);
        var bucketsBefore = GetBuckets(dict);
        for (var i = 0; i < 100; i++) dict.Add(i, i);
        var bucketsAfter = GetBuckets(dict);

        bucketsAfter.Should().BeGreaterThan(bucketsBefore, "插入 100 个元素应触发扩容");
        dict.Count.Should().Be(100);
        for (var i = 0; i < 100; i++) {
            dict.TryGetValue(i, out var v).Should().BeTrue();
            v.Should().Be(i);
        }
    }

    [Fact]
    public void Bug_CopyTo_KvpArray() {
        var dict = new SwissTable<int, int>();
        for (var i = 0; i < 10; i++) dict.Add(i, i);

        var array = new KeyValuePair<int, int>[10];
        ((System.Collections.Generic.ICollection<KeyValuePair<int, int>>)dict).CopyTo(array, 0);

        var keys = array.Select(kv => kv.Key).OrderBy(x => x).ToArray();
        keys.Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
    }

    [Fact]
    public void Bug_EnsureCapacity_PreventsFutureGrowth() {
        var dict = new SwissTable<int, int>();
        dict.EnsureCapacity(1000);

        var bucketsBefore = GetBuckets(dict);
        for (var i = 0; i < 500; i++) dict.Add(i, i);
        var bucketsAfter = GetBuckets(dict);

        bucketsAfter.Should().Be(bucketsBefore,
            "EnsureCapacity(1000) 后插入 500 个元素不应触发扩容");
    }

    private static int GetBuckets(SwissTable<int, int> dict) {
        var rawTableField = typeof(SwissTable<int, int>).GetField("rawTable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var rawTable = rawTableField.GetValue(dict)!;
        var bucketMaskField = rawTable.GetType().GetField("_bucket_mask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var bucketMask = (int)bucketMaskField.GetValue(rawTable)!;
        return bucketMask + 1;
    }
}
