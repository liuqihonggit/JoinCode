namespace Core.Context.Compact;

/// <summary>
/// 压缩上下文 — 在中间件管道中流转的共享状态
/// </summary>
public sealed class CompactContext
{
    /// <summary>压缩请求</summary>
    public required CompactRequest Request { get; init; }

    /// <summary>压缩前 Token 数</summary>
    public int PreCompactTokens { get; set; }

    /// <summary>压缩结果（第一个成功的中间件设置此值后，后续中间件可跳过）</summary>
    public CompactResult? Result { get; set; }

    /// <summary>是否已被某个中间件处理（后续中间件可据此跳过）</summary>
    public bool IsHandled => Result is not null;

    /// <summary>连续失败计数</summary>
    public int ConsecutiveFailures { get; set; }
}
