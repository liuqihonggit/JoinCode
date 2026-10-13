namespace McpToolRegistry;

/// <summary>
/// 工具美学中间件 — Order=870 — 工具执行后分析结果，注入情绪反馈引导 AI 行为
/// 设计哲学：工具本身提供情绪价值，让笨 AI 通过情绪标点感知代码品味
/// 挂载位置：ToolFixHookMiddleware(860) 之后、PostToolUseHookMiddleware 之前、ToolExecutionMiddleware(900) 之前
/// 执行模式：先调 next 让工具执行，再分析 context.Result 注入情绪反馈到 InjectedMessages
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ToolAestheticMiddleware : ServiceEntity, IToolExecutionMiddleware {
    private readonly IAestheticAnalyzer[] _analyzers;
    private readonly ILogger<ToolAestheticMiddleware> _logger;

    /// <summary>
    /// 构造工具美学中间件 — 注入所有美学分析器和日志记录器
    /// </summary>
    /// <param name="analyzers">美学分析器集合 — DI 自动注入所有 IAestheticAnalyzer 实现</param>
    /// <param name="logger">日志记录器实例</param>
    public ToolAestheticMiddleware(
        IEnumerable<IAestheticAnalyzer> analyzers,
        ILogger<ToolAestheticMiddleware> logger) {
        _analyzers = [.. analyzers];
        _logger = logger;
    }

    /// <summary>
    /// 错误处理行为 — Continue 表示美学中间件自身不中断管道
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 先调 next 让工具执行，再遍历所有分析器注入情绪反馈到 InjectedMessages
    /// </summary>
    /// <param name="context">工具执行上下文</param>
    /// <param name="next">下一层中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(
        ToolExecutionContext context,
        MiddlewareDelegate<ToolExecutionContext> next,
        CancellationToken ct) {
        await next(context, ct).ConfigureAwait(false);

        if (context.Result is null) return;
        if (_analyzers.Length == 0) return;

        var injectedMessages = context.Result.InjectedMessages ?? [];

        foreach (var analyzer in _analyzers) {
            try {
                if (!analyzer.TryAnalyze(context, out var feedback)) continue;

                var message = new ApiMessage(
                    MessageRole.User,
                    $"[情绪反馈] {feedback.Message}" +
                    (feedback.Suggestion is not null ? $"\n{suggestionPrefix}{feedback.Suggestion}" : ""));

                injectedMessages = [.. injectedMessages, message];

                _logger.LogInformation("情绪反馈: {Emotion} - {Message}",
                    feedback.Emotion, feedback.Message);
            } catch (Exception ex) {
                _logger.LogWarning(ex, "美学分析器 {AnalyzerType} 异常，跳过", analyzer.GetType().Name);
            }
        }

        if (injectedMessages.Count > 0) {
            context.Result = context.Result with { InjectedMessages = injectedMessages };
        }
    }

    private const string suggestionPrefix = "💡 建议: ";
}
