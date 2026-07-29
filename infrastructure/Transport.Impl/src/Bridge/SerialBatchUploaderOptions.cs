namespace JoinCode.Transport.Bridge;

/// <summary>
/// SerialBatchEventUploader 配置选项
/// </summary>
public sealed record SerialBatchUploaderOptions
{
    /// <summary>最大批次大小</summary>
    public int MaxBatchSize { get; init; } = 500;

    /// <summary>最大队列深度 — 满队列时触发背压</summary>
    public int MaxQueueSize { get; init; } = 100_000;

    /// <summary>指数退避基础延迟（毫秒）</summary>
    public int BaseDelayMs { get; init; } = 500;

    /// <summary>指数退避最大延迟（毫秒）</summary>
    public int MaxDelayMs { get; init; } = 8000;

    /// <summary>抖动范围（毫秒）</summary>
    public int JitterMs { get; init; } = 1000;

    /// <summary>最大连续失败次数 — 超过则丢弃批次</summary>
    public int? MaxConsecutiveFailures { get; init; }

    /// <summary>批次丢弃回调（batchSize, consecutiveFailures）</summary>
    public Action<int, int>? OnBatchDropped { get; init; }

    /// <summary>最大批次字节数 — 0 表示不限制</summary>
    public int MaxBatchBytes { get; init; }
}
