namespace JoinCode.Abstractions.Hooks;

public interface ISessionStartHookManager : IHookManager, IHookHandler<SessionStartHookContext, SessionStartHookResult> {
    /// <summary>会话启动钩子。</summary>
    /// <param name="context">会话启动上下文。</param>
    /// <param name="ct">取消令牌。</param>
    Task<SessionStartHookResult> OnSessionStartAsync(SessionStartHookContext context, CancellationToken ct = default);

    /// <summary>IHookHandler.ExecuteAsync → 委托到 OnSessionStartAsync</summary>
    Task<SessionStartHookResult> IHookHandler<SessionStartHookContext, SessionStartHookResult>.ExecuteAsync(
        SessionStartHookContext context, CancellationToken cancellationToken)
        => OnSessionStartAsync(context, cancellationToken);
}

public sealed partial class SessionStartHookContext {
    /// <summary>获取会话标识。</summary>
    public required string SessionId { get; init; }
    /// <summary>获取启动来源。</summary>
    public required string Source { get; init; }
    /// <summary>获取配置字典。</summary>
    public Dictionary<string, JsonElement> Configuration { get; init; } = new();
}

public sealed partial class SessionStartHookResult {
    /// <summary>获取是否继续执行。</summary>
    public bool ShouldProceed { get; init; } = true;
    /// <summary>获取提示消息。</summary>
    public string? Message { get; init; }
    /// <summary>
    /// 会话启动时要注入模型上下文的文本（多条 hook 以空行拼接）。
    /// 对齐基准 SessionStart 的 <c>hookSpecificOutput.additionalContext</c>：
    /// 不落 transcript，作为部署方的长期指令随系统提示一起给模型看。
    /// </summary>
    public string? AdditionalContext { get; init; }
    /// <summary>
    /// 会话启动时要插入的首条用户消息。本仓无「无输入自动开轮」模式，故只透传不消费
    /// （见 TASK130 A6：自动插入会凭空产生用户回合与模型调用）。
    /// </summary>
    public string? InitialUserMessage { get; init; }
    /// <summary>获取附加配置字典（hook 的 <c>updatedInput</c> 等非上下文类输出）。</summary>
    public Dictionary<string, JsonElement> AdditionalConfig { get; init; } = new();
}
