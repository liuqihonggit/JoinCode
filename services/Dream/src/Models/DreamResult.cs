namespace JoinCode.Dream;

/// <summary>
/// 做梦执行结果
/// </summary>
public sealed record DreamResult
{
    /// <summary>
    /// 是否执行成功
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 是否被跳过（如门控检查未通过）
    /// </summary>
    public bool IsSkipped { get; init; }

    /// <summary>
    /// 结果内容（整合后的记忆内容或跳过/错误信息）
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// 任务ID
    /// </summary>
    public string? TaskId { get; init; }

    /// <summary>
    /// 处理的会话数量
    /// </summary>
    public int SessionsProcessed { get; init; }

    /// <summary>
    /// 执行耗时（毫秒）
    /// </summary>
    public long ExecutionTimeMs { get; init; }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static DreamResult Success(string content, string taskId, int sessionsProcessed, long executionTimeMs)
    {
        return new DreamResult
        {
            IsSuccess = true,
            IsSkipped = false,
            Content = content,
            TaskId = taskId,
            SessionsProcessed = sessionsProcessed,
            ExecutionTimeMs = executionTimeMs
        };
    }

    /// <summary>
    /// 创建跳过结果
    /// </summary>
    public static DreamResult Skipped(string reason)
    {
        return new DreamResult
        {
            IsSuccess = false,
            IsSkipped = true,
            Content = reason
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static DreamResult Failure(string error)
    {
        return new DreamResult
        {
            IsSuccess = false,
            IsSkipped = false,
            Content = error
        };
    }
}
