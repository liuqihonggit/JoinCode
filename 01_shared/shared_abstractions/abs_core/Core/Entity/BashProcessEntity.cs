namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Shell 命令进程实体 — 派生自 ToolExecutionEntity，追踪 Shell 命令进程生命周期
/// 超时检测+强制终止，避免僵尸进程
/// </summary>
public sealed class BashProcessEntity : ToolExecutionEntity
{
    public int? ProcessId { get; set; }
    public string? Command { get; init; }
    public string? WorkingDirectory { get; init; }
    public BashProcessStatus Status { get; set; } = BashProcessStatus.Running;
    public int? ExitCode { get; set; }

    public BashProcessEntity(
        int? processId = null,
        string? command = null,
        string? workingDirectory = null,
        string? toolUseId = null,
        string? spanId = null,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(ObjectType.ShellCommand, "bash", toolUseId, spanId, displayName ?? command ?? $"pid:{processId}", sessionId)
    {
        ProcessId = processId;
        Command = command;
        WorkingDirectory = workingDirectory;
    }

    protected override void OnDispose()
    {
        base.OnDispose();
    }

    /// <summary>
    /// 回收判定 — Shell 命令进程已完成（有 ExitCode）且已持久化
    /// </summary>
    public override bool CanReclaim()
    {
        return base.CanReclaim() && ExitCode.HasValue;
    }

    /// <summary>
    /// 跨会话深拷贝 — 新 ObjectId + 目标会话，深拷贝进程特有字段 + 基类字段
    /// ProcessId/Command/WorkingDirectory 原样拷贝（进程已结束，仅作历史记录）
    /// </summary>
    public override Entity Clone(CloneContext context)
    {
        var cloned = new BashProcessEntity(
            processId: ProcessId,
            command: Command,
            workingDirectory: WorkingDirectory,
            toolUseId: ToolUseId,
            spanId: SpanId,
            displayName: DisplayName,
            sessionId: context.TargetSessionId)
        {
            Status = Status,
            ExitCode = ExitCode,
        };
        ApplyCloneState(cloned, context);
        return cloned;
    }
}

/// <summary>
/// Shell 命令进程状态
/// </summary>
public enum BashProcessStatus
{
    [EnumValue("running")] Running,
    [EnumValue("exited")] Exited,
    [EnumValue("killed")] Killed,
    [EnumValue("timed_out")] TimedOut,
}
