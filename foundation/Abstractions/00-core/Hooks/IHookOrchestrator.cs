namespace JoinCode.Abstractions.Hooks;

/// <summary>
/// 钩子编排器接口（消费方面）
/// 主入口点，执行钩子并返回结果
/// </summary>
public interface IHookOrchestrator
{
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
}
