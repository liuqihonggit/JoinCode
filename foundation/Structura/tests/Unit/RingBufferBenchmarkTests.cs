namespace Structura.Tests;

[Trait("Category", "Benchmark")]
public class RingBufferBenchmarkTests
{
    private const int TotalWrites = 1_000_000;

    [Theory]
    [InlineData(20)]
    [InlineData(200)]
    [InlineData(2000)]
    public void AddOnly_RingBuffer_FasterThanListRemoveRange(int capacity)
    {
        var ringMs = TimeAddOnlyRingBuffer(capacity);
        var listMs = TimeAddOnlyListRemoveRange(capacity);

        ringMs.Should().BeLessThan(listMs,
            $"RingBuffer 应比 List+RemoveRange 快 (capacity={capacity}): Ring={ringMs}ms List={listMs}ms");
    }

    [Theory]
    [InlineData(20)]
    [InlineData(200)]
    [InlineData(2000)]
    public void AddAndSequentialRead_RingBuffer_FasterThanListRemoveRange(int capacity)
    {
        var ringMs = TimeAddAndReadRingBuffer(capacity);
        var listMs = TimeAddAndReadListRemoveRange(capacity);

        ringMs.Should().BeLessThan(listMs,
            $"RingBuffer 应比 List+RemoveRange 快 (capacity={capacity}): Ring={ringMs}ms List={listMs}ms");
    }

    [Fact]
    public void AddOnly_RingBuffer_LargeVolume_CompletesUnder100ms()
    {
        var ms = TimeAddOnlyRingBuffer(200);
        ms.Should().BeLessThan(100, $"RingBuffer 大量写入应在100ms内完成: {ms}ms");
    }

    private static long TimeAddOnlyRingBuffer(int capacity)
    {
        var buf = new RingBuffer<int>(capacity);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < TotalWrites; i++)
            buf.Add(i);
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static long TimeAddOnlyListRemoveRange(int capacity)
    {
        var list = new List<int>(capacity);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < TotalWrites; i++)
        {
            list.Add(i);
            if (list.Count > capacity)
                list.RemoveRange(0, list.Count - capacity);
        }
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static long TimeAddAndReadRingBuffer(int capacity)
    {
        var buf = new RingBuffer<int>(capacity);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < TotalWrites; i++)
        {
            buf.Add(i);
            if ((i & 0x3FF) == 0)
            {
                var sum = 0;
                for (var j = 0; j < buf.Count; j++)
                    sum += buf[j];
            }
        }
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static long TimeAddAndReadListRemoveRange(int capacity)
    {
        var list = new List<int>(capacity);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < TotalWrites; i++)
        {
            list.Add(i);
            if (list.Count > capacity)
                list.RemoveRange(0, list.Count - capacity);
            if ((i & 0x3FF) == 0)
            {
                var sum = 0;
                for (var j = 0; j < list.Count; j++)
                    sum += list[j];
            }
        }
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }
}
