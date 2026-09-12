namespace JoinCode.Transport.Bridge;

/// <summary>
/// 子进程退出状态 — 对齐 TS 端 SessionDoneStatus
/// </summary>
public enum BridgeSubprocessStatus
{
    /// <summary>正常完成（退出码 0）</summary>
    [EnumValue("completed")] Completed,
    /// <summary>失败（非零退出码）</summary>
    [EnumValue("failed")] Failed,
    /// <summary>被中断（SIGTERM/SIGINT）</summary>
    [EnumValue("interrupted")] Interrupted
}
