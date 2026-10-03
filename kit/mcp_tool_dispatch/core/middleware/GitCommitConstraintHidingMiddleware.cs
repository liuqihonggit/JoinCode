namespace McpToolRegistry;

/// <summary>
/// git_commit 约束回显拦截中间件 — 检查 commit 消息是否回显了元指令/规则约束，
/// 若违规则拒绝执行并提示 AI 重新写。防御 AI 把约束暴露在 commit 消息里。
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class GitCommitConstraintHidingMiddleware : ServiceEntity, IToolExecutionMiddleware {
    private const string GitCommitToolName = "git_commit";
    private const string MessageParam = "message";
    private const string ConstraintEchoKey = "commit-constraint-echo";

    private static readonly Regex[] ConstraintEchoPatterns = [
        new(@"\([^)]*(?:不含|不放|不删|不提|无.{0,4}版|非.{0,4}版|按.{0,6}要求|隐匿|省略若干)[^)]*\)", RegexOptions.None, TimeSpan.FromMilliseconds(500)),
        new(@"\[[^]]*(?:free|无|非|不含)[^]]*\]", RegexOptions.None, TimeSpan.FromMilliseconds(500)),
        new(@"按用户要求|面向.{0,6}汇报|给领导看", RegexOptions.None, TimeSpan.FromMilliseconds(500)),
    ];

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
                context.Deny(BuildDenyReason());
                return;
            }
        }
        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 检查 git_commit 消息是否匹配约束回显模式。
    /// </summary>
    /// <param name="context">工具执行上下文</param>
    /// <returns>true 表示 commit 消息疑似回显约束；false 表示正常</returns>
    internal static bool IsConstraintEchoCommit(ToolExecutionContext context) {
        if (!string.Equals(context.ToolName, GitCommitToolName, StringComparison.Ordinal))
            return false;
        if (!context.Arguments.TryGetValue(MessageParam, out var msgEl) || msgEl.ValueKind != JsonValueKind.String)
            return false;
        var commitMsg = msgEl.GetString();
        if (string.IsNullOrEmpty(commitMsg))
            return false;
        foreach (var pattern in ConstraintEchoPatterns) {
            if (pattern.IsMatch(commitMsg))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 构建拒绝原因 — 提示 AI 重新写 commit 消息，不要回显约束。
    /// </summary>
    /// <returns>拒绝原因文本，含错误示例和正确示例</returns>
    internal static string BuildDenyReason() {
        return """
commit 消息疑似回显了元指令/规则约束，已拦截。commit 消息只描述变更本身，不要回显约束。

错误示例（禁止）：
- "feat: xxx (不含 Y)"
- "feat: xxx (无分支名版)"
- "feat: xxx [branch-free]"
- "feat: xxx（已按要求隐匿某约束）"
- "feat: 按用户要求调整 xxx"

正确示例：
- "feat: xxx"
- "fix: 修复 Y 边界"

请重新写 commit 消息，只描述做了什么变更，不提任何约束/规则/指令。
""";
    }
}
