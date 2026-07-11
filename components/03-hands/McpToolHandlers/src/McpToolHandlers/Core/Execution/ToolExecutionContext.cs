
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

    /// <summary>遥测 Span</summary>
    public ITelemetrySpan? Span { get; set; }
}
