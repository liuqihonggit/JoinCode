namespace Structura.Tests;

/// <summary>
/// 多写者并发下每槽位 seq 标记一致性测试。
/// 用全局 Interlocked 单调计数器为每次 Add 分配唯一序号,写入定长环形缓冲;
/// 多写者下"序号分配"与"槽位 CAS 抢占"是两个独立原子操作,写入顺序不保证等于序号顺序,
/// 故快照不可能严格连续。但每槽位 seq 标记应保证读到完整写入而非新旧值混装,
/// 即快照中不应出现重复元素(每个槽位是唯一的最新写入)。
/// </summary>
public sealed class RingBufferMultiWriterTests
{
    [Fact]
    public void MultiWriter_ToArraySnapshot_NoDuplicates()
    {
        const int capacity = 256;
        const int writers = 4;
        const int writesPerWriter = 100_000;
        var buf = new RingBuffer<int>(capacity);
        var globalSeq = 0;
        var doneSignal = new ManualResetEventSlim(false);
        var errors = new List<string>();
        var errLock = new object();

        var consumer = new Thread(() =>
        {
            try
            {
                while (!doneSignal.IsSet)
                {
                    var snap = buf.ToArray();
                    var set = new HashSet<int>(snap.Length);
                    foreach (var v in snap)
                    {
                        if (!set.Add(v))
                        {
                            lock (errLock)
                            {
                                if (errors.Count < 5)
                                    errors.Add($"快照出现重复元素 {v}, 说明每槽位 seq 标记未正确隔离新旧写入");
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lock (errLock) { if (errors.Count < 5) errors.Add($"消费者异常: {ex.Message}"); }
            }
        }) { IsBackground = true };
        consumer.Start();

        var threadErrors = new List<Exception>();
        var threads = new Thread[writers];
        for (var w = 0; w < writers; w++)
        {
            threads[w] = new Thread(() =>
            {
                try
                {
                    for (var i = 0; i < writesPerWriter; i++)
                    {
                        var seq = Interlocked.Increment(ref globalSeq);
                        buf.Add(seq);
                    }
                }
                catch (Exception ex) { lock (threadErrors) threadErrors.Add(ex); }
            }) { IsBackground = true };
        }

        foreach (var th in threads) th.Start();
        foreach (var th in threads) th.Join(TimeSpan.FromSeconds(30));
        doneSignal.Set();
        consumer.Join(TimeSpan.FromSeconds(5));

        threadErrors.Should().BeEmpty();
        errors.Should().BeEmpty("多写者并发下每槽位 seq 标记必须保证快照无重复(读到完整写入而非混装)");
        buf.Count.Should().Be(capacity);
    }
}