namespace Structura.Tests;

public class RingBufferTests
{
    [Fact]
    public void Constructor_CapacityLessThan1_Throws()
    {
        var act = () => new RingBuffer<int>(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NewBuffer_CountIsZero_IsFullFalse()
    {
        var buf = new RingBuffer<int>(5);
        buf.Count.Should().Be(0);
        buf.Capacity.Should().Be(5);
        buf.IsFull.Should().BeFalse();
    }

    [Fact]
    public void Add_NotFull_CountIncrements()
    {
        var buf = new RingBuffer<int>(3);
        buf.Add(10);
        buf.Add(20);
        buf.Count.Should().Be(2);
        buf.IsFull.Should().BeFalse();
        buf[0].Should().Be(10);
        buf[1].Should().Be(20);
    }

    [Fact]
    public void Add_Full_OverwritesOldest()
    {
        var buf = new RingBuffer<int>(3);
        buf.Add(10);
        buf.Add(20);
        buf.Add(30);
        buf.IsFull.Should().BeTrue();
        buf.Count.Should().Be(3);
        buf[0].Should().Be(10);
        buf[1].Should().Be(20);
        buf[2].Should().Be(30);

        buf.Add(40);
        buf.Count.Should().Be(3);
        buf[0].Should().Be(20);
        buf[1].Should().Be(30);
        buf[2].Should().Be(40);
    }

    [Fact]
    public void Add_MultipleOverwrites_MaintainsOrder()
    {
        var buf = new RingBuffer<int>(3);
        foreach (var v in new[] { 1, 2, 3, 4, 5, 6 })
            buf.Add(v);

        buf.Count.Should().Be(3);
        buf[0].Should().Be(4);
        buf[1].Should().Be(5);
        buf[2].Should().Be(6);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var buf = new RingBuffer<int>(3);
        buf.Add(10);

        var actNeg = () => buf[-1];
        actNeg.Should().Throw<ArgumentOutOfRangeException>();

        var actOver = () => buf[1];
        actOver.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Latest_Oldest_ReturnsCorrectElements()
    {
        var buf = new RingBuffer<int>(3);
        buf.Add(10);
        buf.Add(20);
        buf.Add(30);

        buf.Oldest.Should().Be(10);
        buf.Latest.Should().Be(30);

        buf.Add(40);
        buf.Oldest.Should().Be(20);
        buf.Latest.Should().Be(40);
    }

    [Fact]
    public void Clear_ResetsBuffer()
    {
        var buf = new RingBuffer<int>(3);
        buf.Add(10);
        buf.Add(20);

        buf.Clear();

        buf.Count.Should().Be(0);
        buf.IsFull.Should().BeFalse();
    }

    [Fact]
    public void Enumeration_YieldsOldestToLatest()
    {
        var buf = new RingBuffer<int>(3);
        foreach (var v in new[] { 1, 2, 3, 4, 5 })
            buf.Add(v);

        var items = new List<int>();
        foreach (var item in buf)
            items.Add(item);

        items.Should().Equal(3, 4, 5);
    }

    [Fact]
    public void Slice_ReturnsCorrectRange()
    {
        var buf = new RingBuffer<int>(5);
        foreach (var v in new[] { 1, 2, 3, 4, 5, 6, 7 })
            buf.Add(v);

        var slice = buf.Slice(1, 3).ToList();
        slice.Should().Equal(4, 5, 6);
    }

    [Fact]
    public void Slice_FromStart()
    {
        var buf = new RingBuffer<string>(4);
        buf.Add("a");
        buf.Add("b");
        buf.Add("c");

        buf.Slice(0, 3).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Slice_InvalidRange_Throws()
    {
        var buf = new RingBuffer<int>(3);
        buf.Add(10);
        buf.Add(20);

        var actStart = () => buf.Slice(-1, 1);
        actStart.Should().Throw<ArgumentOutOfRangeException>();

        var actCount = () => buf.Slice(0, -1);
        actCount.Should().Throw<ArgumentOutOfRangeException>();

        var actOver = () => buf.Slice(1, 5);
        actOver.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Add_WithReferenceType_WorksCorrectly()
    {
        var buf = new RingBuffer<string>(2);
        buf.Add("hello");
        buf.Add("world");
        buf.Add("foo");

        buf.Count.Should().Be(2);
        buf[0].Should().Be("world");
        buf[1].Should().Be("foo");
    }

    [Fact]
    public void Indexer_AfterWraparound_AccessCorrectElement()
    {
        var buf = new RingBuffer<double>(4);
        foreach (var v in new[] { 1.1, 2.2, 3.3, 4.4, 5.5 })
            buf.Add(v);

        buf[0].Should().Be(2.2);
        buf[1].Should().Be(3.3);
        buf[2].Should().Be(4.4);
        buf[3].Should().Be(5.5);
    }
}
