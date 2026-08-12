namespace JoinCode.Abstractions.Hooks;

/// <summary>
/// 钩子处理器基接口 — 统一"单事件钩子"模式: Task&lt;TResult&gt; ExecuteAsync(TContext, CancellationToken)
/// 派生接口: ISessionStartHookManager, ISubagentStopHookManager
/// 不适用: ICompactHookManager（双事件）, IPostSamplingCallbackManager（注册+触发）, ISessionHookManager（集合管理）, IQueryStopHookManager（注册+执行）
/// </summary>
public interface IHookHandler<TContext, TResult>
{
    /// <summary>
    /// 执行钩子 — 返回结果含 ShouldProceed 标记
    /// </summary>
    Task<TResult> ExecuteAsync(TContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 钩子管理器标记接口 — 所有 Hook Manager 的共同类型，支持 IEnumerable&lt;IHookManager&gt; 统一解析
/// </summary>
public interface IHookManager;
