namespace McpToolRegistry;

/// <summary>
/// git_commit 约束回显拦截中间件 — 检查 commit 消息是否回显了元指令/规则约束，
/// 若违规则拒绝执行并提示 AI 重新写。防御 AI 把约束暴露在 commit 消息里。
/// 检查逻辑委托给 CommitConstraintEchoChecker，/commit 斜杠命令也调用同一检查器。
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class GitCommitConstraintHidingMiddleware : ServiceEntity, IToolExecutionMiddleware {
    private const string GitCommitToolName = "git_commit";
    private const string MessageParam = "message";
    private const string ConstraintEchoKey = "commit-constraint-echo";

    /// <summary>
    /// 错误处理行为 — Continue 表示注入失败不中断管道
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 工具执行前检查 git_commit 消息是否回显约束，违规则拒绝执行。
    /// </summary>
    /// <param name="context">工具执行上下文</param>
    /// <param name="next">下一层中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct) {
        if (IsConstraintEchoCommit(context)) {
            if (CooldownService.ShouldTrigger(ConstraintEchoKey)) {
                CooldownService.RecordTrigger(ConstraintEchoKey);
                context.Deny(CommitConstraintEchoChecker.BuildDenyReason());
            } else {
                context.Deny("commit 消息疑似回显约束，已拦截（已提示过，详见历史）。请重写commit消息，只描述变更本身。");
            }
            return;
        }
        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 检查 git_commit 消息是否匹配约束回显模式 — 委托给 CommitConstraintEchoChecker。
    /// </summary>
    /// <param name="context">工具执行上下文</param>
    /// <returns>true 表示 commit 消息疑似回显约束；false 表示正常</returns>
    internal static bool IsConstraintEchoCommit(ToolExecutionContext context) {
        if (!string.Equals(context.ToolName, GitCommitToolName, StringComparison.Ordinal))
            return false;
        if (!context.Arguments.TryGetValue(MessageParam, out var msgEl) || msgEl.ValueKind != JsonValueKind.String)
            return false;
        return CommitConstraintEchoChecker.ContainsConstraintEcho(msgEl.GetString());
    }
}
