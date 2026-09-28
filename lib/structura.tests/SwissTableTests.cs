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
}
