namespace McpToolRegistry;

/// <summary>
/// 工具健康评分中间件 — Order=850 — 执行后根据结果更新评分，连续失败时注入提示词提醒LLM换策略
/// 支持黑名单（用户主动禁用）、降权（额外扣分）、超图评分（关联工具共享评分空间）
/// 设计原则：永远不禁用工具，失败多次只注入提示词，由LLM自行决策是否换工具
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ToolHealthScoringMiddleware : ServiceEntity, IToolExecutionMiddleware
{
    private readonly ToolHealthMonitor _monitor;
    private readonly ToolHypergraphScorer _scorer;
    private readonly ILogger<ToolHealthScoringMiddleware> _logger;

    /// <summary>
    /// 构造函数 — 注入健康监控器、超图评分器和日志记录器
    /// </summary>
    /// <param name="monitor">工具健康监控器，负责记录成功/失败与黑名单管理</param>
    /// <param name="scorer">超图评分器，负责关联工具评分共享与链路推荐</param>
    /// <param name="logger">日志记录器实例</param>
    public ToolHealthScoringMiddleware(
        ToolHealthMonitor monitor,
        ToolHypergraphScorer scorer,
        ILogger<ToolHealthScoringMiddleware> logger)
    {
        _monitor = monitor;
        _scorer = scorer;
        _logger = logger;
    }

    /// <summary>
    /// 错误处理行为 — Continue 表示评分中间件自身不中断管道
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 检查黑名单→执行工具→根据结果更新健康评分与超图共享评分→连续失败达阈值时注入提示词建议 LLM 换策略
    /// </summary>
    /// <param name="context">工具执行上下文</param>
    /// <param name="next">下一层中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct)
    {
        if (_monitor.IsBlacklisted(context.ToolName))
        {
            _logger.LogWarning("工具 {ToolName} 已被用户加入黑名单，拒绝执行", context.ToolName);
            context.Result = new ToolResult
            {
                Content = [new ToolContent
                {
                    Type = ToolContentType.Text,
                    Text = $"工具 '{context.ToolName}' 已被用户禁用（黑名单）。" +
                           $"如需重新启用，请在 settings.json 的 toolExecution.blacklistedTools 中移除。"
                }],
                IsError = true
            };
            return;
        }

        var recordBefore = await _monitor.GetRecordAsync(context.ToolName, ct).ConfigureAwait(false);
        var consecutiveFailuresBefore = recordBefore?.ConsecutiveFailures ?? 0;

        await next(context, ct).ConfigureAwait(false);

        if (context.Result is null) return;

        if (context.Result.IsError)
        {
            var errorMsg = context.Result.GetFirstText();
            await _monitor.RecordFailureAsync(context.ToolName, errorMsg, ct).ConfigureAwait(false);
        }
        else
        {
            await _monitor.RecordSuccessAsync(context.ToolName, ct).ConfigureAwait(false);
        }

        var allRecords = await _monitor.GetAllRecordsAsync(ct).ConfigureAwait(false);
        _scorer.UpdateSharedScores(allRecords);

        if (consecutiveFailuresBefore >= _monitor.Config.WarningThreshold && context.Result.IsError)
        {
            var effectiveScore = _scorer.CalculateFinalScore(context.ToolName, recordBefore?.Score ?? 0);
            _logger.LogWarning("工具 {ToolName} 连续失败{Count}次（评分{EffectiveScore}），注入提示词",
                context.ToolName, consecutiveFailuresBefore, effectiveScore);

            var warning = new JoinCode.Abstractions.LLM.Chat.ApiMessage(
                JoinCode.Abstractions.LLM.Chat.MessageRole.User,
                $"[系统提示] 工具 '{context.ToolName}' 已连续失败 {consecutiveFailuresBefore} 次" +
                $"（评分: {effectiveScore}）。建议尝试替代工具或换一种方式完成任务。");

            context.Result = context.Result with
            {
                InjectedMessages = [.. (context.Result.InjectedMessages ?? []), warning]
            };
        }
    }
}
