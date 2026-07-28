namespace JoinCode.Abstractions.Hooks.Session;

/// <summary>
/// 会话钩子管理器接口 — 管理会话级函数钩子的注册和移除
/// </summary>
public interface ISessionHookManager
{
    /// <summary>
    /// 添加会话钩子
    /// </summary>
    Task AddSessionHookAsync(
        string sessionId,
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加函数钩子
    /// </summary>
    Task<string> AddFunctionHookAsync(
        string sessionId,
        HookEvent hookEvent,
        string? matcher,
        Func<HookInput, CancellationToken, Task<HookResult>> callback,
        string? errorMessage = null,
        int? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除函数钩子
    /// </summary>
    Task RemoveFunctionHookAsync(
        string sessionId,
        HookEvent hookEvent,
        string hookId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除会话钩子
    /// </summary>
    Task RemoveSessionHookAsync(
        string sessionId,
        HookEvent hookEvent,
        string? matcher,
        HookCommand hook,
        CancellationToken cancellationToken = default);
}
