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
        buf.Capacity.Should().BeGreaterThanOrEqualTo(5);
        buf.IsFull.Should().BeFalse();
        buf.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Add_NotFull_CountIncrements()
    {
        var buf = new RingBuffer<int>(8);
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
        var buf = new RingBuffer<int>(4);
        var cap = buf.Capacity;
        for (var i = 1; i <= cap; i++)
            buf.Add(i * 10);
        buf.IsFull.Should().BeTrue();
        buf.Count.Should().Be(cap);
        buf[0].Should().Be(10);
        buf[^1].Should().Be(cap * 10);

        buf.Add(999);
        buf.Count.Should().Be(cap);
        buf[0].Should().Be(20);
        buf[^1].Should().Be(999);
    }

    [Fact]
    public void Add_MultipleOverwrites_MaintainsOrder()
    {
        var buf = new RingBuffer<int>(4);
        var cap = buf.Capacity;
        for (var v = 1; v <= cap + 3; v++)
            buf.Add(v);
        buf.Count.Should().Be(cap);
        for (var i = 0; i < cap; i++)
            buf[i].Should().Be(cap + 4 - cap + i);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var buf = new RingBuffer<int>(4);
        buf.Add(10);
        var actNeg = () => buf[-1];
        actNeg.Should().Throw<ArgumentOutOfRangeException>();
        var actOver = () => buf[1];
        actOver.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Latest_Oldest_ReturnsCorrectElements()
    {
        var buf = new RingBuffer<int>(4);
        var cap = buf.Capacity;
        for (var i = 0; i < cap; i++)
            buf.Add(i * 10);
        buf.Oldest.Should().Be(0);
        buf.Latest.Should().Be((cap - 1) * 10);
        buf.Add(999);
        buf.Oldest.Should().Be(10);
        buf.Latest.Should().Be(999);
    }

    [Fact]
    public void Clear_ResetsBuffer()
    {
        var buf = new RingBuffer<int>(4);
        buf.Add(10);
        buf.Add(20);
        buf.Clear();
        buf.Count.Should().Be(0);
        buf.IsFull.Should().BeFalse();
        buf.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Enumeration_YieldsOldestToLatest()
    {
        var buf = new RingBuffer<int>(4);
        var cap = buf.Capacity;
        for (var v = 1; v <= cap + 2; v++)
            buf.Add(v);
        var items = new List<int>();
        foreach (var item in buf)
            items.Add(item);
        items.Should().BeInAscendingOrder();
        items[0].Should().Be(3);
        items[^1].Should().Be(cap + 2);
    }

    [Fact]
    public void Slice_ReturnsCorrectRange()
    {
        var buf = new RingBuffer<int>(4);
        var cap = buf.Capacity;
        for (var v = 1; v <= cap + 2; v++)
            buf.Add(v);
        var slice = buf.Slice(1, 3).ToList();
        slice.Should().HaveCount(3);
        slice[0].Should().Be(buf[1]);
        slice[2].Should().Be(buf[3]);
    }

    [Fact]
    public void Slice_FromStart()
    {
        var buf = new RingBuffer<string>(8);
        buf.Add("a");
        buf.Add("b");
        buf.Add("c");
        buf.Slice(0, 3).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Slice_InvalidRange_Throws()
    {
        var buf = new RingBuffer<int>(4);
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
        var buf = new RingBuffer<string>(4);
        var cap = buf.Capacity;
        for (var i = 0; i < cap; i++)
            buf.Add($"item{i}");
        buf.Add("overflow");
        buf.Count.Should().Be(cap);
        buf[^1].Should().Be("overflow");
        buf[0].Should().Be("item1");
    }

    [Fact]
    public void Indexer_AfterWraparound_AccessCorrectElement()
    {
        var buf = new RingBuffer<double>(4);
        var cap = buf.Capacity;
        for (var i = 1; i <= cap + 1; i++)
            buf.Add(i * 1.1);
        buf.Count.Should().Be(cap);
        buf[0].Should().Be(2.2);
        buf[^1].Should().Be((cap + 1) * 1.1);
    }

    [Fact]
    public void ToArray_ReturnsConsistentSnapshot()
    {
        var buf = new RingBuffer<int>(4);
        var cap = buf.Capacity;
        for (var v = 1; v <= cap; v++)
            buf.Add(v);
        buf.ToArray().Should().BeInAscendingOrder();
        buf.Add(cap + 1);
        var snap = buf.ToArray();
        snap.Should().HaveCount(cap);
        snap[^1].Should().Be(cap + 1);
        snap[0].Should().Be(2);
    }

    [Fact]
    public void TryEnqueue_TryDequeue_FifoOrder()
    {
        var buf = new RingBuffer<int>(8);
        buf.TryEnqueue(10).Should().BeTrue();
        buf.TryEnqueue(20).Should().BeTrue();
        buf.TryEnqueue(30).Should().BeTrue();
        buf.Count.Should().Be(3);

        buf.TryDequeue(out var a).Should().BeTrue();
        a.Should().Be(10);
        buf.TryDequeue(out var b).Should().BeTrue();
        b.Should().Be(20);
        buf.TryDequeue(out var c).Should().BeTrue();
        c.Should().Be(30);
        buf.IsEmpty.Should().BeTrue();
        buf.TryDequeue(out _).Should().BeFalse();
    }

    [Fact]
    public void TryEnqueue_Full_ReturnsFalse()
    {
        var buf = new RingBuffer<int>(4);
        var cap = buf.Capacity;
        for (var i = 0; i < cap; i++)
            buf.TryEnqueue(i).Should().BeTrue();
        buf.IsFull.Should().BeTrue();
        buf.TryEnqueue(999).Should().BeFalse();
    }

    [Fact]
    public void EnqueueBatch_DequeueBatch_WorkCorrectly()
    {
        var buf = new RingBuffer<int>(8);
        var input = new[] { 1, 2, 3, 4, 5 };
        var enqueued = buf.EnqueueBatch(input, 0, 5);
        enqueued.Should().Be(5);

        var output = new int[3];
        var dequeued = buf.DequeueBatch(output, 0, 3);
        dequeued.Should().Be(3);
        output.Should().Equal(1, 2, 3);
        buf.Count.Should().Be(2);
    }

    [Fact]
    public void MultiThread_ConcurrentAdd_NoException()
    {
        var buf = new RingBuffer<int>(512);
        var cap = buf.Capacity;
        var threads = new Thread[4];
        var errors = new List<Exception>();
        for (var t = 0; t < 4; t++)
        {
            var threadId = t;
            threads[t] = new Thread(() =>
            {
                try
                {
                    for (var i = 0; i < 10_000; i++)
                        buf.Add(threadId * 10_000 + i);
                }
                catch (Exception ex) { lock (errors) errors.Add(ex); }
            }) { IsBackground = true };
        }
        foreach (var th in threads) th.Start();
        foreach (var th in threads) th.Join();
        errors.Should().BeEmpty();
        buf.Count.Should().Be(cap);
    }
}
