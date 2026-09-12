namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 系统提醒管理器接口 — 管理对话中的动态提醒
/// </summary>
public interface ISystemReminderManager
{
    /// <summary>
    /// 异步添加提醒
    /// </summary>
    Task AddReminderAsync(string id, string content, int priority = 0, CancellationToken ct = default);

    /// <summary>
    /// 异步移除提醒
    /// </summary>
    Task RemoveReminderAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// 异步获取所有提醒（按优先级排序）
    /// </summary>
    Task<IReadOnlyList<SystemReminder>> GetRemindersAsync(CancellationToken ct = default);

    /// <summary>
    /// 异步清除所有提醒
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// 异步将提醒格式化为XML标签形式
    /// </summary>
    Task<string> FormatAsSystemRemindersAsync(CancellationToken ct = default);
}
