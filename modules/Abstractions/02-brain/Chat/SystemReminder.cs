namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 系统提醒 - 在对话中动态插入的提醒信息
/// </summary>
public sealed class SystemReminder
{
    public string Id { get; }
    public string Content { get; }
    public DateTimeOffset CreatedAt { get; }
    public int Priority { get; }

    public SystemReminder(string id, string content, int priority = 0)
    {
        Id = id;
        Content = content;
        CreatedAt = DateTimeOffset.Now;
        Priority = priority;
    }
}
