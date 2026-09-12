
namespace McpToolRegistry;

/// <summary>
/// 工具执行中间件共享上下文 — 在管道各阶段间传递状态
/// </summary>
public sealed class ToolExecutionContext
{
    /// <summary>工具名称</summary>
    public required string ToolName { get; init; }

    /// <summary>工具参数 — 可被中间件修改（如参数修复）</summary>
    public required Dictionary<string, JsonElement> Arguments { get; set; }

    /// <summary>工具处理器 — 由获取阶段设置</summary>
    public IToolHandler? Handler { get; set; }

    /// <summary>进度回调</summary>
    public ToolProgressCallback? OnProgress { get; init; }

    /// <summary>当前 Agent 权限模式</summary>
    public PermissionMode AgentMode { get; init; } = PermissionMode.Auto;

    /// <summary>执行结果 — 由终端执行器或短路中间件设置</summary>
    public ToolResult? Result { get; set; }

    /// <summary>是否已短路 — 中间件设置 Result 后应标记为 true</summary>
    public bool IsShortCircuited => Result is not null;

    /// <summary>权限决策结果 — 权限中间件设置,替代异常传播。默认 Allowed</summary>
    public PermissionDecision PermissionDecision { get; set; } = PermissionDecision.Allowed;

    /// <summary>权限拒绝原因 — PermissionDecision 为 Denied 时填充</summary>
    public string? PermissionDenyReason { get; set; }

    /// <summary>权限确认提示 — PermissionDecision 为 PendingConfirmation 时填充</summary>
    public string? PermissionConfirmationPrompt { get; set; }

    /// <summary>权限确认规则内容 — WebFetch 等 domain:hostname 格式,用于域名级白名单持久化</summary>
    public string? PermissionRuleContent { get; set; }

    /// <summary>遥测 Span</summary>
    public ITelemetrySpan? Span { get; set; }

    /// <summary>工具执行实体 — 长时间运行工具的 Entity 实例，与 LoggingScopeMiddleware (w3) 配合</summary>
    public ToolExecutionEntity? ExecutionEntity { get; set; }

    /// <summary>
    /// 拒绝执行 — 设置权限决策为 Denied,填充错误结果并短路管道(不调 next)
    /// </summary>
    public void Deny(string reason)
    {
        PermissionDecision = PermissionDecision.Denied;
        PermissionDenyReason = reason;
        Result = new ToolResult
        {
            Content = [new() { Type = ToolContentType.Text, Text = reason }],
            IsError = true,
            PermissionDecision = PermissionDecision.Denied
        };
    }

    /// <summary>
    /// 要求确认 — 设置权限决策为 PendingConfirmation,填充提示信息并短路管道(不调 next)
    /// Result 带 PendingConfirmation 标记,由 PermissionAwareToolExecutor 返回给上层触发确认流程
    /// </summary>
    public void RequireConfirmation(string prompt, string? ruleContent = null)
    {
        PermissionDecision = PermissionDecision.PendingConfirmation;
        PermissionConfirmationPrompt = prompt;
        PermissionRuleContent = ruleContent;
        Result = new ToolResult
        {
            Content = [new() { Type = ToolContentType.Text, Text = $"工具 '{ToolName}' 需要确认: {prompt}" }],
            IsError = true,
            PermissionDecision = PermissionDecision.PendingConfirmation,
            ConfirmationPrompt = prompt,
            PermissionRuleContent = ruleContent
        };
    }
}
