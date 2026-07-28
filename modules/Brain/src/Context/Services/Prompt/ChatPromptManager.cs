namespace Core.Context;

/// <summary>
/// 聊天提示词管理器 — 负责系统提示词构建、提醒管理和提示词重建
/// 提取自 ChatService 中的 _systemPromptBuilder + _reminderManager 使用逻辑
/// </summary>
[Register(typeof(IChatPromptManager))]
public sealed partial class ChatPromptManager : IChatPromptManager
{
    [Inject] private readonly SystemPromptBuilder _systemPromptBuilder;
    [Inject] private readonly ISystemReminderManager _reminderManager;
    [Inject] private readonly ILogger<ChatPromptManager>? _logger;

    /// <summary>
    /// 获取分区后的静态前缀（用于清空/压缩后重建系统提示词）
    /// </summary>
    public async Task<string> GetStaticPrefixAsync()
    {
        var (staticPrefix, _) = await _systemPromptBuilder.BuildPartitionedAsync().ConfigureAwait(false);
        return staticPrefix;
    }

    /// <summary>
    /// 清除提示词缓存
    /// </summary>
    public void ClearCache()
    {
        _systemPromptBuilder.ClearCache();
    }

    /// <summary>
    /// 清除所有提醒
    /// </summary>
    public async Task ClearRemindersAsync(CancellationToken cancellationToken = default)
    {
        await _reminderManager.ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加系统提醒
    /// </summary>
    public async Task AddReminderAsync(string id, string content, int priority = 0, CancellationToken cancellationToken = default)
    {
        await _reminderManager.AddReminderAsync(id, content, priority, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("[SystemReminder] 已添加提醒: {ReminderId}", id);
    }

    /// <summary>
    /// 移除系统提醒
    /// </summary>
    public async Task RemoveReminderAsync(string id, CancellationToken cancellationToken = default)
    {
        await _reminderManager.RemoveReminderAsync(id, cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("[SystemReminder] 已移除提醒: {ReminderId}", id);
    }
}
