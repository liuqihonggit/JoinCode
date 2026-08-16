namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 管道 Pre Hook 委托 — 管道执行前调用
/// 返回 true 继续执行，false 短路跳过管道
/// </summary>
public delegate Task<bool> PipelinePreHookDelegate<TContext>(TContext context, CancellationToken ct);

/// <summary>
/// 管道 Post Hook 委托 — 管道执行后调用（无论成功或失败）
/// </summary>
public delegate Task PipelinePostHookDelegate<TContext>(TContext context, CancellationToken ct);
