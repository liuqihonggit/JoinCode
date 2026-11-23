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
    /// <summary>获取附加配置字典。</summary>
    public Dictionary<string, JsonElement> AdditionalConfig { get; init; } = new();
}
