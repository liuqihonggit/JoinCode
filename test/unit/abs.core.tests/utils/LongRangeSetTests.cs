namespace Abs.Tests.Utils;

public sealed class LongRangeSetTests {
    [Fact]
    public void Empty_IsEmptyWithZeroCount() {
        LongRangeSet.Empty.IsEmpty.Should().BeTrue();
        LongRangeSet.Empty.Count.Should().Be(0);
        LongRangeSet.Empty.RangeCount.Should().Be(0);
    }

    [Fact]
    public void Add_SingleValue_CreatesOneRange() {
        var s = LongRangeSet.Empty.Add(5);
        s.Contains(5).Should().BeTrue();
        s.Count.Should().Be(1);
        s.RangeCount.Should().Be(1);
    }

    [Fact]
    public void Add_ConsecutiveValues_MergesIntoSingleRange() {
        var s = LongRangeSet.Empty;
        for (var i = 1; i <= 100; i++) s = s.Add(i);
        s.Count.Should().Be(100);
        s.RangeCount.Should().Be(1);
        s.Contains(1).Should().BeTrue();
        s.Contains(100).Should().BeTrue();
        s.Contains(50).Should().BeTrue();
        s.Contains(0).Should().BeFalse();
        s.Contains(101).Should().BeFalse();
    }

    [Fact]
    public void Add_NonConsecutiveValues_CreatesMultipleRanges() {
        var s = LongRangeSet.Empty.Add(1).Add(2).Add(3).Add(10).Add(11);
        s.Count.Should().Be(5);
        s.RangeCount.Should().Be(2);
    }

    [Fact]
    public void Add_AlreadyPresent_NoChange() {
        var s = LongRangeSet.Empty.Add(1).Add(2).Add(3);
        var s2 = s.Add(2);
        s2.Should().Be(s);
        s2.Count.Should().Be(3);
        s2.RangeCount.Should().Be(1);
    }

    [Fact]
    public void Add_BridgingTwoRanges_MergesThreeIntoOne() {
        var s = LongRangeSet.Empty.Add(1).Add(3);
        s.RangeCount.Should().Be(2);
        var s2 = s.Add(2);
        s2.RangeCount.Should().Be(1);
        s2.Count.Should().Be(3);
    }

    [Fact]
    public void Remove_MiddleValue_SplitsRange() {
        var s = LongRangeSet.Empty.Add(1).Add(2).Add(3);
        var s2 = s.Remove(2);
        s2.Contains(2).Should().BeFalse();
        s2.Contains(1).Should().BeTrue();
        s2.Contains(3).Should().BeTrue();
        s2.RangeCount.Should().Be(2);
        s2.Count.Should().Be(2);
    }

    [Fact]
    public void Remove_Endpoint_ShrinksRange() {
        var s = LongRangeSet.Empty.Add(1).Add(2).Add(3);
        var s2 = s.Remove(1);
        s2.Contains(1).Should().BeFalse();
        s2.Contains(2).Should().BeTrue();
        s2.RangeCount.Should().Be(1);
    }

    [Fact]
    public void Remove_Singleton_DeletesRange() {
        var s = LongRangeSet.Empty.Add(5);
        var s2 = s.Remove(5);
        s2.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Remove_NonPresent_NoChange() {
        var s = LongRangeSet.Empty.Add(1).Add(2);
        var s2 = s.Remove(99);
        s2.Should().Be(s);
    }

    [Fact]
    public void Contains_LargeRange_BinarySearchWorks() {
        var s = LongRangeSet.Empty;
        for (var i = 1000; i < 2000; i++) s = s.Add(i);
        s.Contains(1500).Should().BeTrue();
        s.Contains(999).Should().BeFalse();
        s.Contains(2000).Should().BeFalse();
    }

    [Fact]
    public void Enumerate_ReturnsAscending() {
        var s = LongRangeSet.Empty.Add(1).Add(2).Add(10).Add(11);
        var values = s.Enumerate().ToArray();
        values.Should().Equal([1L, 2, 10, 11]);
    }

    [Fact]
    public void Equals_SameRanges_AreEqual() {
        var a = LongRangeSet.Empty.Add(1).Add(2).Add(10);
        var b = LongRangeSet.Empty.Add(10).Add(1).Add(2);
        a.Should().Be(b);
    }

    [Fact]
    public void Equals_DifferentRanges_NotEqual() {
        var a = LongRangeSet.Empty.Add(1).Add(2);
        var b = LongRangeSet.Empty.Add(1).Add(3);
        a.Should().NotBe(b);
    }
}
