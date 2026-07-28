namespace Core.Context;

[Register]
public sealed partial class IdleToolDetector
{
    private int _consecutiveNoToolRounds;
    private readonly int _maxIdleRounds;
    private readonly string _reminderContent;

    /// <summary>连续未使用工具的轮次数</summary>
    public int ConsecutiveNoToolRounds => Volatile.Read(ref _consecutiveNoToolRounds);
    /// <summary>触发提醒的最大空闲轮次阈值</summary>
    public int MaxIdleRounds => _maxIdleRounds;

    /// <summary>初始化空闲工具检测器，指定最大空闲轮次和提醒内容</summary>
    public IdleToolDetector(int maxIdleRounds = 3, string? reminderContent = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxIdleRounds, 1);

        _maxIdleRounds = maxIdleRounds;
        _reminderContent = reminderContent ?? DefaultReminderContent;
    }

    /// <summary>收到LLM响应后更新空闲计数，使用了工具则重置，否则递增</summary>
    public void OnLlmResponse(bool usedTool)
    {
        if (usedTool)
        {
            Volatile.Write(ref _consecutiveNoToolRounds, 0);
        }
        else
        {
            Interlocked.Increment(ref _consecutiveNoToolRounds);
        }
    }

    /// <summary>判断是否已达到空闲阈值，需要注入提醒</summary>
    public bool ShouldInjectReminder()
        => Volatile.Read(ref _consecutiveNoToolRounds) >= _maxIdleRounds;

    /// <summary>获取格式化后的提醒消息，包含当前空闲轮次数</summary>
    public string GetReminderMessage()
    {
        var rounds = Volatile.Read(ref _consecutiveNoToolRounds);
        return string.Format(_reminderContent, rounds);
    }

    /// <summary>重置空闲计数器为零</summary>
    public void Reset()
    {
        Volatile.Write(ref _consecutiveNoToolRounds, 0);
    }

    private const string DefaultReminderContent =
        "你已经有 {0} 轮没有使用工具了。如果需要执行操作，请使用可用工具。如果任务已完成，请告知用户。";
}
