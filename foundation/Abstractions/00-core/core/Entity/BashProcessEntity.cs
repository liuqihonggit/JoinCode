namespace JoinCode.Abstractions.Entity;

/// <summary>
/// Bash 进程实体 — 派生自 ToolExecutionEntity，追踪 Shell/bash 进程生命周期
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
        string? displayName = null)
        : base(ObjectType.Bash, "bash", toolUseId, spanId, displayName ?? command ?? $"pid:{processId}")
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
    /// 回收判定 — Bash 进程已完成（有 ExitCode）且已持久化
    /// </summary>
    public override bool CanReclaim()
    {
        return base.CanReclaim() && ExitCode.HasValue;
    }
}

/// <summary>
/// Bash 进程状态
/// </summary>
public enum BashProcessStatus
{
    [EnumValue("running")] Running,
    [EnumValue("exited")] Exited,
    [EnumValue("killed")] Killed,
    [EnumValue("timed_out")] TimedOut,
}
