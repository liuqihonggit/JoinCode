namespace Core.Prompts;

/// <summary>
/// 系统提醒管理器 - 管理对话中的动态提醒
/// </summary>
[Register(typeof(ISystemReminderManager), ServiceLifetime.Singleton)]
public sealed partial class SystemReminderManager : ISystemReminderManager, IAsyncDisposable {
    private readonly ConcurrentDictionary<string, SystemReminder> _reminders = new(StringComparer.Ordinal);
    private int _disposed;

    /// <summary>
    /// 异步添加提醒
    /// </summary>
    public Task AddReminderAsync(string id, string content, int priority = 0, CancellationToken ct = default) {
        _reminders[id] = new SystemReminder(id, content, priority);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步移除提醒
    /// </summary>
    public Task RemoveReminderAsync(string id, CancellationToken ct = default) {
        _reminders.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步获取所有提醒（按优先级排序）
    /// </summary>
    public Task<IReadOnlyList<SystemReminder>> GetRemindersAsync(CancellationToken ct = default) {
        return Task.FromResult<IReadOnlyList<SystemReminder>>(
            _reminders.Values
                .OrderByDescending(r => r.Priority)
                .ThenBy(r => r.CreatedAt)
                .ToList());
    }

    /// <summary>
    /// 异步清除所有提醒
    /// </summary>
    public Task ClearAsync(CancellationToken ct = default) {
        _reminders.Clear();
        return Task.CompletedTask;
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

    /// <summary>
    /// 异步释放资源。
    /// </summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        return ValueTask.CompletedTask;
    }
}
