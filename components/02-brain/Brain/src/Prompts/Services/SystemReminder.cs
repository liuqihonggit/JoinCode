namespace Core.Prompts;

/// <summary>
/// 系统提醒管理器 - 管理对话中的动态提醒
/// </summary>
[Register(typeof(ISystemReminderManager))]
public sealed partial class SystemReminderManager : ISystemReminderManager, IAsyncDisposable {
    private readonly List<SystemReminder> _reminders = [];
    private readonly AsyncLock _lock = new();

    /// <summary>
    /// 异步添加提醒
    /// </summary>
    public async Task AddReminderAsync(string id, string content, int priority = 0, CancellationToken ct = default) {
        using (await _lock.LockAsync(ct).ConfigureAwait(false)) {
            _reminders.RemoveAll(r => r.Id == id);
            _reminders.Add(new SystemReminder(id, content, priority));
        }
    }

    /// <summary>
    /// 异步移除提醒
    /// </summary>
    public async Task RemoveReminderAsync(string id, CancellationToken ct = default) {
        using (await _lock.LockAsync(ct).ConfigureAwait(false)) {
            _reminders.RemoveAll(r => r.Id == id);
        }
    }

    /// <summary>
    /// 异步获取所有提醒（按优先级排序）
    /// </summary>
    public async Task<IReadOnlyList<SystemReminder>> GetRemindersAsync(CancellationToken ct = default) {
        using (await _lock.LockAsync(ct).ConfigureAwait(false)) {
            return _reminders
                .OrderByDescending(r => r.Priority)
                .ThenBy(r => r.CreatedAt)
                .ToList();
        }
    }

    /// <summary>
    /// 异步清除所有提醒
    /// </summary>
    public async Task ClearAsync(CancellationToken ct = default) {
        using (await _lock.LockAsync(ct).ConfigureAwait(false)) {
            _reminders.Clear();
        }
    }

    /// <summary>
    /// 异步将提醒格式化为XML标签形式
    /// </summary>
    public async Task<string> FormatAsSystemRemindersAsync(CancellationToken ct = default) {
        var reminders = await GetRemindersAsync(ct).ConfigureAwait(false);
        if (reminders.Count == 0) return string.Empty;

        var result = new System.Text.StringBuilder();
        foreach (var reminder in reminders) {
            result.AppendLine($"<system-reminder>");
            result.AppendLine(reminder.Content);
            result.AppendLine($"</system-reminder>");
        }
        return result.ToString().TrimEnd();
    }

    public async ValueTask DisposeAsync()
    {
        _lock.Dispose();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
