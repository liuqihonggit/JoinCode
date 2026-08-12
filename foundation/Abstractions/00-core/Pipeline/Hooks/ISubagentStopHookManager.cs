namespace JoinCode.Abstractions.Hooks;

/// <summary>
/// SubagentStop 钩子管理器 — SubagentStop 事件的单一事件源。
/// 所有 SubagentStop 触发必须走此接口，禁止直接调 IHookOrchestrator.ExecuteHooksAsync(HookEvent.SubagentStop)。
/// </summary>
public interface ISubagentStopHookManager : IHookManager, IHookHandler<SubagentStopHookContext, SubagentStopHookResult>
{
    /// <summary>触发 SubagentStop 钩子 — 返回 ShouldProceed=false 表示阻塞</summary>
    Task<SubagentStopHookResult> OnSubagentStopAsync(SubagentStopHookContext context, CancellationToken ct = default);

    /// <summary>IHookHandler.ExecuteAsync → 委托到 OnSubagentStopAsync</summary>
    Task<SubagentStopHookResult> IHookHandler<SubagentStopHookContext, SubagentStopHookResult>.ExecuteAsync(
        SubagentStopHookContext context, CancellationToken cancellationToken)
        => OnSubagentStopAsync(context, cancellationToken);
}

/// <summary>SubagentStop 钩子上下文</summary>
public sealed partial class SubagentStopHookContext
{
    /// <summary>会话 ID</summary>
    public required string SessionId { get; init; }
    /// <summary>Agent ID</summary>
    public required string AgentId { get; init; }
    /// <summary>Agent 类型（用作 hook matcher）</summary>
    public required string AgentType { get; init; }
    /// <summary>工作树路径（可选）</summary>
    public string? WorktreePath { get; init; }
    /// <summary>是否成功完成</summary>
    public bool IsSuccess { get; init; }
    /// <summary>错误信息（失败时）</summary>
    public string? Error { get; init; }
    /// <summary>执行耗时（毫秒）</summary>
    public long? ExecutionTimeMs { get; init; }
    /// <summary>附加元数据</summary>
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

/// <summary>SubagentStop 钩子结果</summary>
public sealed partial class SubagentStopHookResult
{
    /// <summary>是否继续（true=继续，false=阻塞）</summary>
    public bool ShouldProceed { get; init; } = true;
    /// <summary>消息</summary>
    public string? Message { get; init; }
    /// <summary>附加数据</summary>
    public Dictionary<string, JsonElement> AdditionalData { get; init; } = new();

    /// <summary>继续（放行）</summary>
    public static SubagentStopHookResult Proceed(string? message = null) => new() { ShouldProceed = true, Message = message };
    /// <summary>阻塞</summary>
    public static SubagentStopHookResult Block(string? message = null) => new() { ShouldProceed = false, Message = message };
}
