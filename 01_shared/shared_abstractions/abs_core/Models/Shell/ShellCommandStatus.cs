namespace JoinCode.Abstractions.Models.Shell;

/// <summary>
/// Shell 命令状态 — 对齐 TS ShellCommand.status
/// </summary>
public enum ShellCommandStatus
{
    Running,
    Backgrounded,
    Completed,
    Killed
}
