namespace JoinCode.Transport.Impl.Tests;

/// <summary>
/// SerialBatchEventUploader 确定性测试 — ComputeRetryDelay 退避范围 + ComputeBatchTakeCount 批次切分纯函数。
/// 不触发网络发送（不调用 EnqueueAsync/FlushAsync），仅测纯计算逻辑。
/// </summary>
public class SerialBatchEventUploaderTest {
    private static SerialBatchEventUploader CreateUploader(
        int baseDelay = 100, int maxDelay = 1000, int jitter = 50, int maxBatchSize = 500, int maxBatchBytes = 0) {
        var options = new SerialBatchUploaderOptions {
            BaseDelayMs = baseDelay,
            MaxDelayMs = maxDelay,
            JitterMs = jitter,
            MaxBatchSize = maxBatchSize,
            MaxBatchBytes = maxBatchBytes,
        };
        return new SerialBatchEventUploader(new HttpClient(), "http://localhost/test", options);
    }

    // === ComputeRetryDelay: 指数退避 + 抖动（jitter 随机，断言范围） ===

    /// <summary>无 Retry-After 时按指数退避，返回值落在 [exponential, exponential+jitter]。</summary>
    [Fact]
    public void ComputeRetryDelay_NoRetryAfter_FallsInExponentialRange() {
        using var uploader = CreateUploader(baseDelay: 100, maxDelay: 1000, jitter: 50);

        // failures=1: exponential = Min(100*1, 1000) = 100 → [100, 150]
        uploader.ComputeRetryDelay(1, null).Should().BeInRange(100, 150);
        // failures=2: exponential = Min(100*2, 1000) = 200 → [200, 250]
        uploader.ComputeRetryDelay(2, null).Should().BeInRange(200, 250);
        // failures=3: exponential = Min(100*4, 1000) = 400 → [400, 450]
        uploader.ComputeRetryDelay(3, null).Should().BeInRange(400, 450);
    }

    /// <summary>指数退避被 MaxDelayMs 钳制后加抖动。</summary>
    [Fact]
    public void ComputeRetryDelay_ExceedsMax_ClampedToMaxPlusJitter() {
        using var uploader = CreateUploader(baseDelay: 100, maxDelay: 1000, jitter: 50);

        // failures=5: exponential = Min(100*16, 1000) = 1000 → [1000, 1050]
        uploader.ComputeRetryDelay(5, null).Should().BeInRange(1000, 1050);
    }

    /// <summary>Retry-After 在 [base, max] 区间内时使用 Retry-After + 抖动。</summary>
    [Fact]
    public void ComputeRetryDelay_RetryAfterInRange_UsesRetryAfterPlusJitter() {
        using var uploader = CreateUploader(baseDelay: 100, maxDelay: 1000, jitter: 50);

        // retryAfter=500: clamped = Max(100, Min(500, 1000)) = 500 → [500, 550]
        uploader.ComputeRetryDelay(1, 500).Should().BeInRange(500, 550);
    }

    /// <summary>Retry-After 超过 MaxDelayMs 时被钳制到 MaxDelayMs。</summary>
    [Fact]
    public void ComputeRetryDelay_RetryAfterExceedsMax_ClampedToMax() {
        using var uploader = CreateUploader(baseDelay: 100, maxDelay: 1000, jitter: 50);

        // retryAfter=10000: clamped = Max(100, Min(10000, 1000)) = 1000 → [1000, 1050]
        uploader.ComputeRetryDelay(1, 10000).Should().BeInRange(1000, 1050);
    }

    /// <summary>Retry-After 小于 BaseDelayMs 时被钳制到 BaseDelayMs。</summary>
    [Fact]
    public void ComputeRetryDelay_RetryAfterBelowBase_ClampedToBase() {
        using var uploader = CreateUploader(baseDelay: 100, maxDelay: 1000, jitter: 50);

        // retryAfter=10: clamped = Max(100, Min(10, 1000)) = 100 → [100, 150]
        uploader.ComputeRetryDelay(1, 10).Should().BeInRange(100, 150);
    }

    /// <summary>JitterMs=0 时返回值确定（无抖动）。</summary>
    [Fact]
    public void ComputeRetryDelay_ZeroJitter_Deterministic() {
        using var uploader = CreateUploader(baseDelay: 100, maxDelay: 1000, jitter: 0);

        uploader.ComputeRetryDelay(1, null).Should().Be(100);
        uploader.ComputeRetryDelay(2, null).Should().Be(200);
        uploader.ComputeRetryDelay(5, null).Should().Be(1000);
        uploader.ComputeRetryDelay(1, 500).Should().Be(500);
    }

    // === ComputeRetryDelay / ComputeExponentialDelay: 整数溢出钳制 ===

    /// <summary>failures=32 时位移 1&lt;&lt;31 不溢出为负数（位移钳制到 30）。</summary>
    [Fact]
    public void ComputeRetryDelay_LargeFailures_NoShiftOverflow() {
        using var uploader = CreateUploader(baseDelay: 1, maxDelay: int.MaxValue, jitter: 0);

        // failures=32: shift=min(31,30)=30, 1<<30=1073741824, *1=1073741824（正数，非负）
        uploader.ComputeRetryDelay(32, null).Should().Be(1 << 30);
    }

    /// <summary>failures=100 时位移钳制到 30，结果与 failures=31 一致。</summary>
    [Fact]
    public void ComputeRetryDelay_VeryLargeFailures_CappedAtShift30() {
        using var uploader = CreateUploader(baseDelay: 1, maxDelay: int.MaxValue, jitter: 0);

        uploader.ComputeRetryDelay(100, null).Should().Be(1 << 30);
        uploader.ComputeRetryDelay(31, null).Should().Be(1 << 30);
    }

    /// <summary>BaseDelayMs * (1&lt;&lt;shift) 超过 int 范围时钳制到 MaxDelayMs（不溢出）。</summary>
    [Fact]
    public void ComputeRetryDelay_ProductOverflow_ClampedToMaxDelay() {
        using var uploader = CreateUploader(baseDelay: 100_000, maxDelay: 5000, jitter: 0);

        // failures=20: shift=19→钳30, 1<<19=524288, 100000*524288=52428800000 > int.MaxValue
        // 钳制到 MaxDelayMs=5000
        uploader.ComputeRetryDelay(20, null).Should().Be(5000);
    }

    /// <summary>ComputeExponentialDelay 纯函数: failures=1 返回 baseDelay。</summary>
    [Fact]
    public void ComputeExponentialDelay_FirstFailure_ReturnsBaseDelay() {
        SerialBatchEventUploader.ComputeExponentialDelay(1, 1000, 5000).Should().Be(1000);
    }

    /// <summary>ComputeExponentialDelay 纯函数: failures=0 返回 baseDelay（防御性）。</summary>
    [Fact]
    public void ComputeExponentialDelay_ZeroFailures_ReturnsBaseDelay() {
        SerialBatchEventUploader.ComputeExponentialDelay(0, 1000, 5000).Should().Be(1000);
    }

    /// <summary>ComputeExponentialDelay 纯函数: 负数 failures 返回 baseDelay（防御性）。</summary>
    [Fact]
    public void ComputeExponentialDelay_NegativeFailures_ReturnsBaseDelay() {
        SerialBatchEventUploader.ComputeExponentialDelay(-5, 1000, 5000).Should().Be(1000);
    }

    /// <summary>ComputeExponentialDelay 纯函数: 大位移钳制到 30，结果为正数。</summary>
    [Fact]
    public void ComputeExponentialDelay_HugeFailures_PositiveResult() {
        var result = SerialBatchEventUploader.ComputeExponentialDelay(50, 1, int.MaxValue);
        result.Should().BePositive();
        result.Should().Be(1 << 30);
    }

    /// <summary>ComputeExponentialDelay 纯函数: 乘法溢出时钳制到 maxDelayMs。</summary>
    [Fact]
    public void ComputeExponentialDelay_MultiplicationOverflow_ClampedToMax() {
        // baseDelay=100000, failures=20: 100000 * (1<<19) = 52428800000 > int.MaxValue
        // 钳制到 maxDelayMs=5000
        SerialBatchEventUploader.ComputeExponentialDelay(20, 100_000, 5000).Should().Be(5000);
    }

    /// <summary>ComputeExponentialDelay 纯函数: baseDelay 超过 maxDelay 时直接钳制。</summary>
    [Fact]
    public void ComputeExponentialDelay_BaseExceedsMax_ClampedToMax() {
        SerialBatchEventUploader.ComputeExponentialDelay(1, 10_000, 5000).Should().Be(5000);
    }

    // === ComputeBatchTakeCount: 纯函数，确定性 ===

    /// <summary>MaxBatchBytes=0（不限字节）时按 MaxBatchSize 截取。</summary>
    [Fact]
    public void ComputeBatchTakeCount_NoByteLimit_CappedByMaxBatchSize() {
        var pending = new List<string> { "a", "b", "c" };

        SerialBatchEventUploader.ComputeBatchTakeCount(pending, 500, 0).Should().Be(3);
        SerialBatchEventUploader.ComputeBatchTakeCount(pending, 2, 0).Should().Be(2);
        SerialBatchEventUploader.ComputeBatchTakeCount(pending, 1, 0).Should().Be(1);
    }

    /// <summary>字节限制下按累计字节数截取。</summary>
    [Fact]
    public void ComputeBatchTakeCount_ByteLimit_StopsWhenExceeding() {
        var pending = new List<string> { "aa", "bb", "cc" }; // 各 2 字节

        // MaxBatchBytes=5: 2 + 2 = 4 ≤ 5, 4 + 2 = 6 > 5 → 取 2 条
        SerialBatchEventUploader.ComputeBatchTakeCount(pending, 500, 5).Should().Be(2);
    }

    /// <summary>第一条即使超过 MaxBatchBytes 也始终发送（避免队头饥饿）。</summary>
    [Fact]
    public void ComputeBatchTakeCount_FirstItemExceedsBytes_StillTakesOne() {
        var pending = new List<string> { "aaaaaa", "b" }; // 第一条 6 字节

        // MaxBatchBytes=5: 第一条 6 > 5，但 takeCount 兜底为 1
        SerialBatchEventUploader.ComputeBatchTakeCount(pending, 500, 5).Should().Be(1);
    }

    /// <summary>空队列返回 0。</summary>
    [Fact]
    public void ComputeBatchTakeCount_EmptyPending_ReturnsZero() {
        var pending = new List<string>();

        SerialBatchEventUploader.ComputeBatchTakeCount(pending, 500, 0).Should().Be(0);
        SerialBatchEventUploader.ComputeBatchTakeCount(pending, 500, 100).Should().Be(0);
    }

    /// <summary>MaxBatchSize 与 MaxBatchBytes 双重约束取较小者。</summary>
    [Fact]
    public void ComputeBatchTakeCount_BothLimits_TakesSmaller() {
        var pending = new List<string> { "a", "b", "c", "d" }; // 各 1 字节

        // MaxBatchSize=2, MaxBatchBytes=100: 字节不限，按条数取 2
        SerialBatchEventUploader.ComputeBatchTakeCount(pending, 2, 100).Should().Be(2);
        // MaxBatchSize=500, MaxBatchBytes=3: 1+1+1=3 ≤ 3, 3+1=4 > 3 → 取 3
        SerialBatchEventUploader.ComputeBatchTakeCount(pending, 500, 3).Should().Be(3);
    }

    /// <summary>所有条目累计未超字节上限时全部取走。</summary>
    [Fact]
    public void ComputeBatchTakeCount_AllWithinLimit_TakesAll() {
        var pending = new List<string> { "a", "b", "c" };

        SerialBatchEventUploader.ComputeBatchTakeCount(pending, 500, 100).Should().Be(3);
    }

    /// <summary>单条队列始终取 1（无论字节限制）。</summary>
    [Fact]
    public void ComputeBatchTakeCount_SingleItem_AlwaysTakesOne() {
        var pending = new List<string> { "x" };

        SerialBatchEventUploader.ComputeBatchTakeCount(pending, 500, 0).Should().Be(1);
        SerialBatchEventUploader.ComputeBatchTakeCount(pending, 500, 1).Should().Be(1);
    }

    // === ShouldDropBatch: 连续失败达上限丢弃 ===

    /// <summary>maxConsecutiveFailures=null（不限制）时始终返回 false。</summary>
    [Fact]
    public void ShouldDropBatch_NoLimit_ReturnsFalse() {
        SerialBatchEventUploader.ShouldDropBatch(1, null).Should().BeFalse();
        SerialBatchEventUploader.ShouldDropBatch(100, null).Should().BeFalse();
        SerialBatchEventUploader.ShouldDropBatch(10000, null).Should().BeFalse();
    }

    /// <summary>failures < max 时返回 false（未达上限）。</summary>
    [Fact]
    public void ShouldDropBatch_BelowMax_ReturnsFalse() {
        SerialBatchEventUploader.ShouldDropBatch(1, 5).Should().BeFalse();
        SerialBatchEventUploader.ShouldDropBatch(4, 5).Should().BeFalse();
    }

    /// <summary>failures == max 时返回 true（达到上限）。</summary>
    [Fact]
    public void ShouldDropBatch_EqualsMax_ReturnsTrue() {
        SerialBatchEventUploader.ShouldDropBatch(5, 5).Should().BeTrue();
        SerialBatchEventUploader.ShouldDropBatch(1, 1).Should().BeTrue();
    }

    /// <summary>failures > max 时返回 true（超过上限）。</summary>
    [Fact]
    public void ShouldDropBatch_ExceedsMax_ReturnsTrue() {
        SerialBatchEventUploader.ShouldDropBatch(6, 5).Should().BeTrue();
        SerialBatchEventUploader.ShouldDropBatch(100, 5).Should().BeTrue();
    }

    /// <summary>failures=0 时返回 false（无失败）。</summary>
    [Fact]
    public void ShouldDropBatch_ZeroFailures_ReturnsFalse() {
        SerialBatchEventUploader.ShouldDropBatch(0, 5).Should().BeFalse();
        SerialBatchEventUploader.ShouldDropBatch(0, 1).Should().BeFalse();
    }

    // === ExtractRetryAfterMs: 从异常提取 Retry-After ===

    /// <summary>RetryableError 带 RetryAfterMs 时返回该值。</summary>
    [Fact]
    public void ExtractRetryAfterMs_RetryableErrorWithRetryAfter_ReturnsValue() {
        var ex = new RetryableError("429 Too Many Requests", retryAfterMs: 5000);

        SerialBatchEventUploader.ExtractRetryAfterMs(ex).Should().Be(5000);
    }

    /// <summary>RetryableError 不带 RetryAfterMs 时返回 null。</summary>
    [Fact]
    public void ExtractRetryAfterMs_RetryableErrorWithoutRetryAfter_ReturnsNull() {
        var ex = new RetryableError("Server error: 500");

        SerialBatchEventUploader.ExtractRetryAfterMs(ex).Should().BeNull();
    }

    /// <summary>普通 Exception 返回 null。</summary>
    [Fact]
    public void ExtractRetryAfterMs_PlainException_ReturnsNull() {
        var ex = new InvalidOperationException("not retryable");

        SerialBatchEventUploader.ExtractRetryAfterMs(ex).Should().BeNull();
    }

    /// <summary>TimeoutException 返回 null。</summary>
    [Fact]
    public void ExtractRetryAfterMs_TimeoutException_ReturnsNull() {
        var ex = new TimeoutException("timed out");

        SerialBatchEventUploader.ExtractRetryAfterMs(ex).Should().BeNull();
    }

    /// <summary>RetryableError 嵌套在 AggregateException 中时返回 null（仅顶层类型匹配）。</summary>
    [Fact]
    public void ExtractRetryAfterMs_NestedInAggregate_ReturnsNull() {
        var inner = new RetryableError("inner", retryAfterMs: 3000);
        var agg = new AggregateException(inner);

        // 实现仅检查 ex is RetryableError，不展开 InnerException
        SerialBatchEventUploader.ExtractRetryAfterMs(agg).Should().BeNull();
    }

    /// <summary>RetryAfterMs=0 时返回 0（不视为 null）。</summary>
    [Fact]
    public void ExtractRetryAfterMs_RetryAfterZero_ReturnsZero() {
        var ex = new RetryableError("retry immediately", retryAfterMs: 0);

        SerialBatchEventUploader.ExtractRetryAfterMs(ex).Should().Be(0);
    }
}
