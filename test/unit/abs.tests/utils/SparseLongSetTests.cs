namespace Abs.Tests.Utils;

public sealed class SparseLongSetTests {
    [Fact]
    public void Empty_IsEmptyWithZeroCount() {
        SparseLongSet.Empty.IsEmpty.Should().BeTrue();
        SparseLongSet.Empty.Count.Should().Be(0);
    }

    [Fact]
    public void Add_SingleValue() {
        var s = SparseLongSet.Empty.Add(42);
        s.Contains(42).Should().BeTrue();
        s.Count.Should().Be(1);
    }

    [Fact]
    public void Add_MultipleValues_KeepsSorted() {
        var s = SparseLongSet.Empty.Add(30).Add(10).Add(20);
        s.Enumerate().Should().Equal([10L, 20, 30]);
    }

    [Fact]
    public void Add_Duplicate_NoChange() {
        var s = SparseLongSet.Empty.Add(1).Add(2);
        var s2 = s.Add(1);
        s2.Should().Be(s);
        s2.Count.Should().Be(2);
    }

    [Fact]
    public void Remove_Existing() {
        var s = SparseLongSet.Empty.Add(1).Add(2).Add(3);
        var s2 = s.Remove(2);
        s2.Contains(2).Should().BeFalse();
        s2.Count.Should().Be(2);
        s2.Enumerate().Should().Equal([1L, 3]);
    }

    [Fact]
    public void Remove_NonPresent_NoChange() {
        var s = SparseLongSet.Empty.Add(1).Add(2);
        var s2 = s.Remove(99);
        s2.Should().Be(s);
    }

    [Fact]
    public void Contains_BinarySearch() {
        var s = SparseLongSet.Empty;
        for (var i = 0; i < 100; i++) s = s.Add(i * 10);
        s.Contains(500).Should().BeTrue();
        s.Contains(501).Should().BeFalse();
    }

    [Fact]
    public void EncodeDeltas_Empty_ReturnsEmptyArray() {
        SparseLongSet.Empty.EncodeDeltas().Should().BeEmpty();
    }

    [Fact]
    public void EncodeDeltas_SingleValue_8Bytes() {
        var s = SparseLongSet.Empty.Add(123456);
        var encoded = s.EncodeDeltas();
        encoded.Length.Should().Be(8);
        SparseLongSet.Decode(encoded).Should().Be(s);
    }

    [Fact]
    public void EncodeDecode_RoundTrip() {
        var s = SparseLongSet.Empty.Add(1).Add(5).Add(100).Add(1000).Add(5000);
        var encoded = s.EncodeDeltas();
        var decoded = SparseLongSet.Decode(encoded);
        decoded.Should().Be(s);
    }

    [Fact]
    public void EncodeDeltas_SmallDeltas_CompressesWell() {
        var s = SparseLongSet.Empty;
        for (var i = 0; i < 100; i++) s = s.Add(i + 1);
        var encoded = s.EncodeDeltas();
        encoded.Length.Should().BeLessThan(100 * 8);
        SparseLongSet.Decode(encoded).Should().Be(s);
    }

    [Fact]
    public void Equals_SameValues_AreEqual() {
        var a = SparseLongSet.Empty.Add(1).Add(2).Add(3);
        var b = SparseLongSet.Empty.Add(3).Add(1).Add(2);
        a.Should().Be(b);
    }
}
