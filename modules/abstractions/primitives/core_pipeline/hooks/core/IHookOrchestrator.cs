namespace JoinCode.Abstractions.Hooks;

/// <summary>
/// 钩子编排器接口（消费方面）
/// 主入口点，执行钩子并返回结果
/// </summary>
public interface IHookOrchestrator {
    /// <summary>
    /// 执行钩子
    /// </summary>
    IAsyncEnumerable<HookResult> ExecuteHooksAsync(
        HookInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行特定事件的钩子
    /// </summary>
    IAsyncEnumerable<HookResult> ExecuteHooksAsync(
        HookEvent hookEvent,
        Dictionary<string, JsonElement> payload,
        string? matcher = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册钩子事件处理器 — 将 HookExecutionEvent 转发到外部消费者（如 IEventConsumer → NDJSON 输出）
    /// </summary>
    /// <param name="handler">事件处理器，接收 HookExecutionEvent（HookStartedEvent/HookProgressEvent/HookResponseEvent）</param>
    void RegisterHookEventHandler(Action<HookExecutionEvent> handler);

    /// <summary>
    /// 注销钩子事件处理器
    /// </summary>
    /// <param name="handler">要注销的处理器</param>
    void UnregisterHookEventHandler(Action<HookExecutionEvent> handler);
}