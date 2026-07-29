namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 聊天提示词管理器接口 — 负责系统提示词构建、提醒管理和提示词重建
/// 关系: 消费 ISystemPromptProvider (01-ai) 提供的部分来构建完整提示词；与 IChatContextManager (05-memory) 职责划分：本接口管系统提示词，IChatContextManager 管消息列表
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
