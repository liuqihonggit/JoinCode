namespace Core.Context;

/// <summary>
/// 流式 token 序列检测器 — 共享无锁环形队列 + 后台单线程持续轮询检测
/// 架构类似麦克风采集: 生产者(Ingest)写入共享 RingBuffer(SeqLock 无锁),后台单线程周期性采样快照进行检测
/// 串行多分析器(尾重复→n-gram),漏斗式触发: 先廉价检测后昂贵检测,任一触发即返回
/// </summary>
public sealed class StreamTokenDetector : IDisposable
{
    private readonly RingBuffer<string> _tokenWindow;
    private readonly Thread _detectThread;
    private readonly CancellationTokenSource _cts;
    private readonly TimeSpan _detectInterval;
    private readonly int _minPatternLength;
    private readonly int _requiredRepeats;
    private readonly int _maxPatternLength;
    private volatile LoopDetectionResult? _latestResult;
    private int _triggerCount;
    private bool _disposed;

    /// <summary>
    /// 初始化流式 token 序列检测器
    /// </summary>
    /// <param name="windowCapacity">环形队列容量(存储最近 N 个 token)</param>
    /// <param name="detectInterval">后台线程检测间隔(像麦克风采样周期)</param>
    /// <param name="minPatternLength">最小重复模式长度(token 数)</param>
    /// <param name="requiredRepeats">触发所需的最少重复次数</param>
    /// <param name="maxPatternLength">最大重复模式长度(token 数)</param>
    public StreamTokenDetector(
        int windowCapacity = 500,
        TimeSpan? detectInterval = null,
        int minPatternLength = 3,
        int requiredRepeats = 4,
        int maxPatternLength = 50)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(minPatternLength, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredRepeats, 2);

        _tokenWindow = new RingBuffer<string>(windowCapacity);
        _detectInterval = detectInterval ?? TimeSpan.FromMilliseconds(100);
        _minPatternLength = minPatternLength;
        _requiredRepeats = requiredRepeats;
        _maxPatternLength = maxPatternLength;
        _cts = new CancellationTokenSource();
        _detectThread = new Thread(DetectLoop) { IsBackground = true, Name = "StreamTokenDetector" };
        _detectThread.Start();
    }

    /// <summary>
    /// 生产者: 写入共享无锁环形队列(O(1),非阻塞,SeqLock 内部保证安全)
    /// </summary>
    public void Ingest(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        _tokenWindow.Add(token);
    }

    /// <summary>
    /// 后台消费者: 像麦克风一样持续轮询检测,周期性采样 RingBuffer 快照
    /// </summary>
    private void DetectLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            var result = DetectNow();
            if (result.IsLoopDetected)
            {
                Interlocked.Increment(ref _triggerCount);
                _latestResult = result;
            }

            if (_cts.Token.WaitHandle.WaitOne(_detectInterval))
                break;
        }
    }

    /// <summary>
    /// 同步检测: 获取无锁快照(SeqLock 保证一致性),串行运行多分析器(漏斗式触发)
    /// </summary>
    public LoopDetectionResult DetectNow()
    {
        var snapshot = _tokenWindow.ToArray();
        if (snapshot.Length == 0)
            return LoopDetectionResult.NoLoop;

        var result = DetectTailRepetition(snapshot);
        if (result.IsLoopDetected)
            return result;

        return DetectNgramRepetition(snapshot);
    }

    /// <summary>
    /// 分析器1(最廉价): 尾重复检测 — 检查 token 序列尾部是否有连续重复模式
    /// </summary>
    private LoopDetectionResult DetectTailRepetition(string[] tokens)
    {
        var count = tokens.Length;
        if (count < _minPatternLength * _requiredRepeats)
            return LoopDetectionResult.NoLoop;

        var maxLen = Math.Min(_maxPatternLength, count / _requiredRepeats);
        for (var patternLen = maxLen; patternLen >= _minPatternLength; patternLen--)
        {
            var repeatCount = CountTailRepeats(tokens, patternLen, count);
            if (repeatCount >= _requiredRepeats)
            {
                var pattern = string.Join("→", tokens, count - patternLen, patternLen);
                var loopStart = count - patternLen * repeatCount;
                return new LoopDetectionResult(true, pattern, repeatCount, loopStart, _triggerCount + 1);
            }
        }

        return LoopDetectionResult.NoLoop;
    }

    /// <summary>
    /// 从尾部往回数连续重复次数
    /// </summary>
    private int CountTailRepeats(string[] tokens, int patternLen, int count)
    {
        var repeatCount = 1;
        var pos = count;
        while (pos >= patternLen * 2)
        {
            var currentStart = pos - patternLen;
            var prevStart = currentStart - patternLen;
            if (!RangeEquals(tokens, prevStart, currentStart, patternLen))
                break;
            repeatCount++;
            pos = prevStart + patternLen;
        }
        return repeatCount;
    }

    private static bool RangeEquals(string[] tokens, int offset1, int offset2, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (tokens[offset1 + i] != tokens[offset2 + i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// 分析器2(中等): n-gram 频率检测 — 统计 n-gram 出现频率,高频表示非连续重复
    /// </summary>
    private LoopDetectionResult DetectNgramRepetition(string[] tokens)
    {
        var count = tokens.Length;
        var ngramLen = _minPatternLength;
        if (count < ngramLen * 2)
            return LoopDetectionResult.NoLoop;

        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i <= count - ngramLen; i++)
        {
            var ngram = string.Join("→", tokens, i, ngramLen);
            ref var f = ref CollectionsMarshal.GetValueRefOrAddDefault(freq, ngram, out _);
            f++;
        }

        foreach (var kvp in freq)
        {
            if (kvp.Value >= _requiredRepeats)
                return new LoopDetectionResult(true, kvp.Key, kvp.Value, 0, _triggerCount + 1);
        }

        return LoopDetectionResult.NoLoop;
    }

    /// <summary>
    /// 获取后台线程最新检测结果(null 表示未检测到循环)
    /// </summary>
    public LoopDetectionResult? GetLatestResult() => _latestResult;

    /// <summary>
    /// 当前环形队列中的 token 数
    /// </summary>
    public int TokenCount => _tokenWindow.Count;

    /// <summary>
    /// 循环检测触发次数(由后台线程递增)
    /// </summary>
    public int TriggerCount => _triggerCount;

    /// <summary>
    /// 重置检测器: 清空环形队列和检测结果
    /// </summary>
    public void Reset()
    {
        _tokenWindow.Clear();
        _latestResult = null;
        Interlocked.Exchange(ref _triggerCount, 0);
    }

    /// <summary>
    /// 停止后台线程并释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();
        _detectThread.Join(TimeSpan.FromSeconds(1));
        _cts.Dispose();
    }
}
