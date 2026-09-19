namespace Core.Agents;

/// <summary>
/// Agent Worktree 会话管理 Actor 命令类型 — 每个命令对应一个 AgentWorktreeService 会话操作，由 WorktreeSessionActor Consumer 串行处理。
/// <para>TASK001: AsyncLock 迁移到 Actor 邮箱管道，消除 4 处显式锁（GetSession/GetAllSessions/SaveSession/RemoveSession）。</para>
/// <para>读操作（GetSessionAsync/GetAllSessionsAsync）也经 Actor，因 _sessions 是 Dictionary 非线程安全。</para>
/// </summary>
public abstract record WorktreeSessionCommand;

/// <summary>获取单个会话 — 对应 GetSessionAsync</summary>
public sealed record GetSessionCmd(
    string AgentId,
    TaskCompletionSource<AgentWorktreeSession?> Reply) : WorktreeSessionCommand;

/// <summary>获取所有会话 — 对应 GetAllSessionsAsync</summary>
public sealed record GetAllSessionsCmd(
    TaskCompletionSource<IReadOnlyList<AgentWorktreeSession>> Reply) : WorktreeSessionCommand;

/// <summary>保存会话 — 对应 SaveSessionAsync</summary>
public sealed record SaveSessionCmd(
    AgentWorktreeSession Session,
    TaskCompletionSource Reply) : WorktreeSessionCommand;

/// <summary>移除会话 — 对应 RemoveSessionAsync</summary>
public sealed record RemoveSessionCmd(
    string AgentId,
    TaskCompletionSource Reply) : WorktreeSessionCommand;