namespace Core.Query;

/// <summary>
/// 请求前上下文预算守卫 — query 循环版 ADR 0156（对齐 chat 循环 GuardContextBudget）。
/// <para>每轮 LLM 请求前估算实时历史占比：≥0.7 预防性裁剪到 65% 窗口，≥1.0 紧急裁剪到 50%。</para>
/// <para>HistorySnipMiddleware 只在工具调用后触发，单轮内工具输出突增仍可能越界；本中间件补请求前检查。</para>
/// </summary>
[Register(typeof(IQueryMiddleware), ServiceLifetime.Singleton)]
public sealed partial class PreRequestBudgetGuardMiddleware : ServiceEntity, IQueryMiddleware {
    private const double PreventiveThreshold = 0.7;
    private const double EmergencyThreshold = 1.0;
    private const double PreventiveTargetRatio = 0.65;
    private const double EmergencyTargetRatio = 0.5;
    // ADR 0156 内容类型感知估算：CJK 1.5 字符/token，非 CJK 4 字符/token，每条消息 +4 结构开销
    private const double CjkCharsPerToken = 1.5;
    private const double NonCjkCharsPerToken = 4.0;
    private const double MessageOverheadTokens = 4.0;

    private readonly IHistorySnipService? _historySnipService;
    private readonly IChatContextManager? _contextManager;

    /// <summary>
    /// 构造函数 — 依赖缺失时不注册守卫（保持非 DI 场景行为不变）。
    /// </summary>
    /// <param name="historySnipService">历史裁剪服务（可选）</param>
    /// <param name="contextManager">上下文管理器 — 提供当前模型上下文窗口（可选）</param>
    public PreRequestBudgetGuardMiddleware(
        IHistorySnipService? historySnipService = null,
        IChatContextManager? contextManager = null) {
        _historySnipService = historySnipService;
        _contextManager = contextManager;
    }

    /// <summary>错误处理策略 — 继续执行</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册迭代前预算守卫钩子。
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下一委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct) {
        if (_historySnipService is not null && _contextManager is not null) {
            context.BeforeIterationHooks.Add(GuardAsync);
        }

        return next(context, ct);
    }

    private async Task GuardAsync(QueryMiddlewareContext context, CancellationToken ct) {
        var snipService = _historySnipService ?? throw new InvalidOperationException("History snip service not available.");
        var contextManager = _contextManager ?? throw new InvalidOperationException("Context manager not available.");

        var ctxMax = contextManager.GetContextMaxTokens();
        if (ctxMax <= 0 || context.ChatHistory.Count == 0) {
            return;
        }

        var estimated = EstimateHistoryTokens(context.ChatHistory);
        var ratio = (double)estimated / ctxMax;
        if (ratio <= PreventiveThreshold) {
            return;
        }

        var emergency = ratio >= EmergencyThreshold;
        var target = (int)(ctxMax * (emergency ? EmergencyTargetRatio : PreventiveTargetRatio));
        var result = await snipService.SnipByTokenLimitAsync(context.ChatHistory, target, ct).ConfigureAwait(false);
        var level = emergency ? "紧急" : "预防性";
        context.Logger?.LogInformation("[QueryEngine] 请求前预算守卫（{Level}）: 估算 {Estimated}/{CtxMax} token（{Ratio:P0}），裁剪 {Removed} 条",
            level, estimated, ctxMax, ratio, result.MessagesRemoved);
        Diag.WriteLine($"[BUDGET-GUARD] 请求前{level}裁剪: ratio={ratio:P0}, 目标={target}, 移除={result.MessagesRemoved}");
    }

    /// <summary>估算历史 token 占用 — ADR 0156 内容类型感知（CJK/非 CJK 分率 + 消息结构开销）。</summary>
    private static long EstimateHistoryTokens(MessageList history) {
        var cjkTokens = 0.0;
        var nonCjkTokens = 0.0;
        foreach (var message in history) {
            if (!string.IsNullOrEmpty(message.Content)) {
                var cjkChars = 0;
                var nonCjkChars = 0;
                foreach (var ch in message.Content) {
                    if (IsCjkChar(ch)) {
                        cjkChars++;
                    } else {
                        nonCjkChars++;
                    }
                }
                cjkTokens += cjkChars / CjkCharsPerToken;
                nonCjkTokens += nonCjkChars / NonCjkCharsPerToken;
            }
            cjkTokens += MessageOverheadTokens;
        }
        return (long)(cjkTokens + nonCjkTokens);
    }

    /// <summary>判断字符是否为 CJK 字符（中文/日文/韩文）</summary>
    private static bool IsCjkChar(char c) =>
        c >= 0x4E00 && c <= 0x9FFF
        || c >= 0x3040 && c <= 0x30FF
        || c >= 0xAC00 && c <= 0xD7AF
        || c >= 0x3400 && c <= 0x4DBF;
}
