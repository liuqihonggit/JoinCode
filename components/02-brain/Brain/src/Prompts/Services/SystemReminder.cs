namespace Core.Prompts;

/// <summary>
/// 系统提醒管理器 - 管理对话中的动态提醒
/// </summary>
[Register(typeof(ISystemReminderManager))]
public sealed partial class SystemReminderManager : ISystemReminderManager, IAsyncDisposable {
    private readonly List<SystemReminder> _reminders = [];
    private readonly SemaphoreSlim _lock;

    public SystemReminderManager()
    {
        _lock = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// 异步添加提醒
    /// </summary>
    public async Task AddReminderAsync(string id, string content, int priority = 0, CancellationToken ct = default) {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try {
            // 如果已存在相同ID的提醒，先移除
            _reminders.RemoveAll(r => r.Id == id);
            _reminders.Add(new SystemReminder(id, content, priority));
        }
        finally {
            _lock.Release();
        }
    }

    /// <summary>
    /// 异步移除提醒
    /// </summary>
    public async Task RemoveReminderAsync(string id, CancellationToken ct = default) {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try {
            _reminders.RemoveAll(r => r.Id == id);
        }
        finally {
            _lock.Release();
        }
    }

    /// <summary>
    /// 异步获取所有提醒（按优先级排序）
    /// </summary>
    public async Task<IReadOnlyList<SystemReminder>> GetRemindersAsync(CancellationToken ct = default) {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try {
            return _reminders
                .OrderByDescending(r => r.Priority)
                .ThenBy(r => r.CreatedAt)
                .ToList();
        }
        finally {
            _lock.Release();
        }
    }

    /// <summary>
    /// 异步清除所有提醒
    /// </summary>
    public async Task ClearAsync(CancellationToken ct = default) {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try {
            _reminders.Clear();
        }
        finally {
            _lock.Release();
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
