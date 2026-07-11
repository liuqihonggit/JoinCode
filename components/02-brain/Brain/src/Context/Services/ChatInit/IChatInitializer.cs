namespace Core.Context;

/// <summary>
/// 初始化器接口 — 会话初始化、配置变更监听
/// </summary>
public interface IChatInitializer
{
    /// <summary>
    /// 初始化会话
    /// </summary>
    Task InitializeAsync(ToolUseContext toolUseContext, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存当前成本
    /// </summary>
    Task SaveCurrentCostsAsync(string sessionId, CancellationToken cancellationToken = default);
}
