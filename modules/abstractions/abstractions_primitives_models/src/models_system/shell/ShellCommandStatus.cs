namespace JoinCode.Abstractions.Models.Shell;

/// <summary>
/// Shell 命令状态 — 对齐 TS ShellCommand.status
/// </summary>
public enum ShellCommandStatus {
    [EnumValue("running")] Running,
    [EnumValue("backgrounded")] Backgrounded,
    [EnumValue("completed")] Completed,
    [EnumValue("killed")] Killed
}