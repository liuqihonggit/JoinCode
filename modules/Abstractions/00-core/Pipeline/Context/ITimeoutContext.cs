namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 超时上下文 — 支持通用 TimeoutMiddleware 复用
/// </summary>
public interface ITimeoutContext : IPipelineContext
{
    /// <summary>超时时长 — 中间件在此时间内未完成则取消</summary>
    TimeSpan Timeout { get; }

    /// <summary>是否已超时 — 超时后由中间件设置</summary>
    bool IsTimedOut { get; set; }
}
