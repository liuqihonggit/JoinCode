namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 系统提醒 - 在对话中动态插入的提醒信息
/// </summary>
public sealed class SystemReminder {
    /// <summary>获取提醒标识。</summary>
    public string Id { get; }
    /// <summary>获取提醒内容。</summary>
    public string Content { get; }
    /// <summary>获取创建时间。</summary>
    public DateTimeOffset CreatedAt { get; }
    /// <summary>获取优先级。</summary>
    public int Priority { get; }

    /// <summary>构造系统提醒。</summary>
    /// <param name="id">提醒标识。</param>
    /// <param name="content">提醒内容。</param>
    /// <param name="priority">优先级。</param>
    public SystemReminder(string id, string content, int priority = 0) {
        Id = id;
        Content = content;
        CreatedAt = DateTimeOffset.Now;
        Priority = priority;
    }
}
