namespace Infrastructure.Pipeline.Middlewares;


/// <summary>
/// 通用崩溃快照中间件 — 捕获管道异常自动记录 CrashSnapshot 后重新抛出
/// 注册为管道第一个中间件（最外层），零侵入覆盖所有异常
/// 扩展：支持 Result.IsError 路径记录快照（可选）+ CooldownService 防重复
/// </summary>
public sealed class CrashSnapshotMiddleware<TContext> : IMiddleware<TContext> {
    private readonly ICrashSnapshotStore _store;
    private readonly string _pipelineName;
    private readonly Func<TContext, CrashExecutionContext?>? _contextExtractor;
    private readonly Func<TContext, bool>? _isErrorSelector;
    private readonly Func<TContext, string?>? _errorMsgSelector;

    /// <summary>
    /// 构造崩溃快照中间件
    /// </summary>
    /// <param name="store">崩溃快照存储</param>
    /// <param name="pipelineName">管道名称，用于标识快照来源</param>
    /// <param name="contextExtractor">上下文提取器，从 TContext 提取执行上下文信息</param>
    /// <param name="isErrorSelector">错误结果检测器，当 TContext 有 Result.IsError 语义时传入（如 ToolExecutionContext）</param>
    /// <param name="errorMsgSelector">错误消息提取器，从 TContext 提取错误消息文本</param>
    public CrashSnapshotMiddleware(
        ICrashSnapshotStore store,
        string pipelineName,
        Func<TContext, CrashExecutionContext?>? contextExtractor = null,
        Func<TContext, bool>? isErrorSelector = null,
        Func<TContext, string?>? errorMsgSelector = null) {
        ArgumentException.ThrowIfNullOrEmpty(pipelineName);
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _pipelineName = pipelineName;
        _contextExtractor = contextExtractor;
        _isErrorSelector = isErrorSelector;
        _errorMsgSelector = errorMsgSelector;
    }

    /// <summary>
    /// 错误处理行为 — Continue 表示快照记录失败不中断管道
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行管道下一环节；异常路径记录快照后重新抛出；Result.IsError 路径（可选）经 CooldownService 防重复后记录快照
    /// </summary>
    /// <param name="context">管道上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct) {
        try {
            await next(context, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) { throw; } catch (Exception ex) {
            RecordSnapshot(context, ex);
            throw;
        }

        if (_isErrorSelector is null || !_isErrorSelector(context)) return;

        var cooldownKey = $"crash-{_pipelineName}";
        if (!CooldownService.ShouldTrigger(cooldownKey)) return;
        CooldownService.RecordTrigger(cooldownKey);

        var errorMsg = _errorMsgSelector?.Invoke(context);
        RecordSnapshot(context, new InvalidOperationException(errorMsg ?? "执行失败"));
    }

    private void RecordSnapshot(TContext context, Exception ex) {
        var execCtx = _contextExtractor?.Invoke(context)
            ?? new CrashExecutionContext { OperationName = _pipelineName };

        _store.Add(new CrashSnapshot(_pipelineName, CrashSeverity.Error, ex, execCtx));
    }
}
