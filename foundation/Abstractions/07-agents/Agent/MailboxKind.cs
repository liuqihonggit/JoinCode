namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 邮箱类型 — 消息传递的通道选择。
/// 对齐 TS 原版 的 teammateMailbox（文件）+ AgentTool（进程内）双模式。
/// </summary>
public enum MailboxKind
{
    /// <summary>进程内邮箱（InProcessMailbox，内存 Channel 直传，同步 subagent）。</summary>
    InProcess,

    /// <summary>文件邮箱（TeammateMailboxService，文件持久化，跨进程 teammate swarm）。</summary>
    File,
}
