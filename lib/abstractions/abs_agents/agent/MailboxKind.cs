namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 邮箱类型 — 消息传递的通道选择，四种通道统一路由 — ADR 0111。
/// </summary>
public enum MailboxKind
{
    /// <summary>进程内邮箱（InProcessMailbox，内存 Channel 直传，同步 subagent）。</summary>
    [EnumValue("in_process")]
    InProcess,

    /// <summary>文件邮箱（FileMailbox，文件持久化，跨进程 teammate swarm）。</summary>
    [EnumValue("file")]
    File,

    /// <summary>有名管道邮箱（NamedPipeMailbox，跨进程双工实时通信）— ADR 0108。</summary>
    [EnumValue("named_pipe")]
    NamedPipe,

    /// <summary>网络邮箱（NetworkMailbox，QQ/飞书/Discord 等外部平台）— ADR 0110。</summary>
    [EnumValue("network")]
    Network,
}
