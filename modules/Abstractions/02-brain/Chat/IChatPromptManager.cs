namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 聊天提示词管理器接口 — 负责系统提示词构建、提醒管理和提示词重建
/// </summary>
public interface IChatPromptManager
{
    /// <summary>
    /// 获取分区后的静态前缀（用于清空/压缩后重建系统提示词）
    /// </summary>
    Task<string> GetStaticPrefixAsync();

    /// <summary>
    /// 清除提示词缓存
    /// </summary>
    void ClearCache();

    /// <summary>
    /// 清除所有提醒
    /// </summary>
    Task ClearRemindersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加系统提醒
    /// </summary>
    Task AddReminderAsync(string id, string content, int priority = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除系统提醒
    /// </summary>
    Task RemoveReminderAsync(string id, CancellationToken cancellationToken = default);
}
