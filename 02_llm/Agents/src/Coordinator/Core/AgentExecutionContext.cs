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
    /// 执行结果 — 归纳原 IsSuccess(bool?)+IsCancelled(bool) 的合法组合
    /// <para>消除 IsSuccess=true&amp;IsCancelled=true 非法组合</para>
    /// </summary>
    public AgentOutcome Outcome { get; set; } = AgentOutcome.Pending;

    /// <summary>
    /// 执行模式
    /// </summary>
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.Single;
}

/// <summary>
/// Agent 执行结果 — 归纳原 IsSuccess(bool?)+IsCancelled(bool)
/// </summary>
public enum AgentOutcome
{
    /// <summary>未完成 — 原 IsSuccess=null, IsCancelled=false</summary>
    [EnumValue("pending")] Pending,

    /// <summary>成功 — 原 IsSuccess=true, IsCancelled=false</summary>
    [EnumValue("succeeded")] Succeeded,

    /// <summary>失败 — 原 IsSuccess=false, IsCancelled=false</summary>
    [EnumValue("failed")] Failed,

    /// <summary>已取消 — 原 IsCancelled=true</summary>
    [EnumValue("cancelled")] Cancelled
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
