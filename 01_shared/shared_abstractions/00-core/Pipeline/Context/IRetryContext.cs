namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 重试上下文 — 支持通用 RetryMiddleware 复用
/// </summary>
public interface IRetryContext : IPipelineContext
{
    /// <summary>最大重试次数（不含首次执行）</summary>
    int MaxRetries { get; }

    /// <summary>当前已重试次数 — 由中间件维护</summary>
    int RetryCount { get; set; }

    /// <summary>最后一次异常 — 由中间件设置</summary>
    Exception? LastError { get; set; }

    /// <summary>判断异常是否可重试 — 各 Context 自行决定重试策略</summary>
    bool IsRetryable(Exception ex);
}
