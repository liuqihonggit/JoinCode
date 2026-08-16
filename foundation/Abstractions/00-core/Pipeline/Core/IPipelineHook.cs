namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 管道 Hook 标记接口 — 用于标识管道级别的 Pre/Post Hook
/// </summary>
public interface IPipelineHook<TContext> { }

/// <summary>
/// 管道 Pre Hook — 管道执行前调用，返回 false 则短路跳过管道
/// </summary>
public interface IPipelinePreHook<TContext> : IPipelineHook<TContext>
{
    Task<bool> InvokeAsync(TContext context, CancellationToken ct);
}

/// <summary>
/// 管道 Post Hook — 管道执行后调用（无论成功或失败）
/// </summary>
public interface IPipelinePostHook<TContext> : IPipelineHook<TContext>
{
    Task InvokeAsync(TContext context, CancellationToken ct);
}
