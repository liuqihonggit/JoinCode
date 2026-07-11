namespace Core.Agents.Coordinator;

/// <summary>
/// Agent执行上下文 - 跟踪Agent的执行状态和元数据
/// </summary>
public sealed class AgentExecutionContext
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// 任务描述
    /// </summary>
    public required string Task { get; init; }

    /// <summary>
    /// 生成时间
    /// </summary>
    public DateTime SpawnedAt { get; init; }

    /// <summary>
    /// 最后执行开始时间
    /// </summary>
    public DateTime? LastExecutionStart { get; set; }

    /// <summary>
    /// 最后执行结束时间
    /// </summary>
    public DateTime? LastExecutionEnd { get; set; }

    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool? IsSuccess { get; set; }

    /// <summary>
    /// 是否被取消
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// 执行模式
    /// </summary>
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.Single;
}

/// <summary>
/// 执行模式
/// </summary>
public enum ExecutionMode
{
    /// <summary>
    /// 单Agent执行
    /// </summary>
    [EnumValue("single")] Single,

    /// <summary>
    /// 并行执行
    /// </summary>
    [EnumValue("parallel")] Parallel,

    /// <summary>
    /// 串行执行
    /// </summary>
    [EnumValue("sequential")] Sequential
}
