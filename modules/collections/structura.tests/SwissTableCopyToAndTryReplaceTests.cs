// JCC11003 抑制: 底层实现/测试代码, ! 用于可空抑制, 是必要用法
#pragma warning disable JCC11003
namespace Structura.Tests;

public class SwissTableCopyToTests {
    private static SwissTable<string, int> MakeDict(params (string k, int v)[] items) {
        var dict = new SwissTable<string, int>();
        foreach (var (k, v) in items) dict.Add(k, v);
        return dict;
    }

    [Fact]
    public void CopyToKvp_WritesAllEntries() {
        var dict = MakeDict(("a", 1), ("b", 2), ("c", 3));
        var array = new KeyValuePair<string, int>[3];
        dict.CopyToKvp(array, 0);
        array.Should().Contain(new KeyValuePair<string, int>("a", 1));
        array.Should().Contain(new KeyValuePair<string, int>("b", 2));
        array.Should().Contain(new KeyValuePair<string, int>("c", 3));
    }

    [Fact]
    public void CopyToKvp_WithOffset_WritesAtCorrectPosition() {
        var dict = MakeDict(("x", 10));
        var array = new KeyValuePair<string, int>[3];
        dict.CopyToKvp(array, 1);
        array[1].Should().Be(new KeyValuePair<string, int>("x", 10));
        array[0].Should().Be(default(KeyValuePair<string, int>));
        array[2].Should().Be(default(KeyValuePair<string, int>));
    }

    [Fact]
    public void CopyToKvp_EmptyDict_WritesNothing() {
        var dict = new SwissTable<string, int>();
        var array = new KeyValuePair<string, int>[0];
        var act = () => dict.CopyToKvp(array, 0);
        act.Should().NotThrow();
    }

    [Fact]
    public void CopyToDictEntry_WritesAllEntriesAsDictionaryEntry() {
        var dict = MakeDict(("a", 1), ("b", 2));
        var array = new DictionaryEntry[2];
        dict.CopyToDictEntry(array, 0);
        array.Select(e => (e.Key!.ToString(), e.Value)).Should().Contain(("a", 1));
        array.Select(e => (e.Key!.ToString(), e.Value)).Should().Contain(("b", 2));
    }

    [Fact]
    public void CopyToDictEntry_WithOffset_WritesAtCorrectPosition() {
        var dict = MakeDict(("k", 42));
        var array = new DictionaryEntry[3];
        dict.CopyToDictEntry(array, 2);
        array[2].Key.Should().Be("k");
        array[2].Value.Should().Be(42);
    }

    [Fact]
    public void CopyToObjectBox_WritesBoxedKeyValuePairs() {
        var dict = MakeDict(("a", 1), ("b", 2));
        var array = new object[2];
        dict.CopyToObjectBox(array, 0);
        array[0].Should().BeOfType<KeyValuePair<string, int>>();
        array[1].Should().BeOfType<KeyValuePair<string, int>>();
        array.Cast<KeyValuePair<string, int>>().Should().Contain(new KeyValuePair<string, int>("a", 1));
        array.Cast<KeyValuePair<string, int>>().Should().Contain(new KeyValuePair<string, int>("b", 2));
    }

    [Fact]
    public void CopyToObjectBox_WithOffset_WritesAtCorrectPosition() {
        var dict = MakeDict(("z", 99));
        var array = new object[2];
        dict.CopyToObjectBox(array, 1);
        array[1].Should().Be(new KeyValuePair<string, int>("z", 99));
        array[0].Should().BeNull();
    }
}

public class SwissTableTryReplaceExistingTests {
    private SwissTable<string, int>.Entry _entry;

    private static SwissTable<string, int>.Entry MakeEntry(string key, int value) =>
        new() { Key = key, Value = value };

    [Fact]
    public void TryReplaceExisting_OverwriteExisting_UpdatesKeyAndValue() {
        var dict = new SwissTable<string, int>();
        _entry = MakeEntry("old", 1);
        var result = dict.TryReplaceExisting(ref _entry, "new", 2, SwissTable<string, int>.InsertionBehavior.OverwriteExisting);
        result.Should().BeTrue();
        _entry.Key.Should().Be("new");
        _entry.Value.Should().Be(2);
    }

    [Fact]
    public void TryReplaceExisting_None_ReturnsFalseWithoutChange() {
        var dict = new SwissTable<string, int>();
        _entry = MakeEntry("keep", 42);
        var result = dict.TryReplaceExisting(ref _entry, "ignored", 99, SwissTable<string, int>.InsertionBehavior.None);
        result.Should().BeFalse();
        _entry.Key.Should().Be("keep");
        _entry.Value.Should().Be(42);
    }

    [Fact]
    public void TryReplaceExisting_ThrowOnExisting_ThrowsException() {
        var dict = new SwissTable<string, int>();
        _entry = MakeEntry("dup", 1);
        var act = () => dict.TryReplaceExisting(ref _entry, "dup", 2, SwissTable<string, int>.InsertionBehavior.ThrowOnExisting);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryReplaceExisting_OverwriteExisting_ReturnsTrue() {
        var dict = new SwissTable<string, int>();
        _entry = MakeEntry("k", 0);
        var result = dict.TryReplaceExisting(ref _entry, "k", 100, SwissTable<string, int>.InsertionBehavior.OverwriteExisting);
        result.Should().BeTrue();
    }
}
