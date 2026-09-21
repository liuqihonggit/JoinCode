namespace JoinCode.Abstractions.Configuration.Execution;

public sealed class ExecutionOptions {
    /// <summary>获取模拟工作耗时(毫秒)。</summary>
    public int SimulatedWorkDurationMs { get; init; } = 5000;
    /// <summary>获取最大并发任务数。</summary>
    public int MaxConcurrentTasks { get; init; } = 12;
    /// <summary>获取是否启用详细日志。</summary>
    public bool VerboseLogging { get; init; } = true;
}