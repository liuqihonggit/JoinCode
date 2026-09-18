namespace Core.Agents.Coordinator;

/// <summary>
/// 团队管理 Actor 命令类型 — 每个命令对应一个 ITeamManager 写操作，由 TeamActor Consumer 串行处理。
/// <para>TASK001: AsyncLock+文件 I/O 迁移到 Actor 邮箱管道，消除 8 处显式锁。</para>
/// <para>读操作（GetTeamAsync/ListTeamsAsync/GetTeamMembersAsync/GetTeamAllowedPathsAsync）不经 Actor，直接读 TeamRegistry（ConcurrentDictionary 线程安全）。</para>
/// </summary>
public abstract record TeamCommand;

/// <summary>添加团队成员 — 对应 AddTeamMemberAsync</summary>
public sealed record AddMemberCmd(
    string TeamId,
    string AgentId,
    TaskCompletionSource<OperationResult<TeamInfo?>> Reply) : TeamCommand;

/// <summary>移除团队成员 — 对应 RemoveTeamMemberAsync</summary>
public sealed record RemoveMemberCmd(
    string TeamId,
    string AgentId,
    TaskCompletionSource<OperationResult<TeamInfo?>> Reply) : TeamCommand;

/// <summary>发送团队消息 — 对应 SendMessageAsync</summary>
public sealed record SendMsgCmd(
    string TeamId,
    string SenderId,
    string Content,
    string? MessageType,
    TaskCompletionSource<OperationResult<TeamInfo?>> Reply) : TeamCommand;

/// <summary>发送私信 — 对应 SendMessageToAgentAsync</summary>
public sealed record SendDirectMsgCmd(
    string TargetAgentId,
    string SenderId,
    string Content,
    string? MessageType,
    TaskCompletionSource<OperationResult<TeamInfo?>> Reply) : TeamCommand;

/// <summary>获取团队消息列表 — 对应 GetTeamMessagesAsync</summary>
public sealed record GetMsgsCmd(
    string TeamId,
    int Limit,
    TaskCompletionSource<IReadOnlyList<TeamMessage>> Reply) : TeamCommand;

/// <summary>广播消息 — 对应 BroadcastMessageAsync</summary>
public sealed record BroadcastMsgCmd(
    string TeamId,
    string SenderId,
    string Content,
    string? MessageType,
    TaskCompletionSource<OperationResult<TeamInfo?>> Reply) : TeamCommand;

/// <summary>设置成员活跃状态 — 对应 SetMemberActiveAsync</summary>
public sealed record SetMemberActiveCmd(
    string TeamId,
    string AgentId,
    bool IsActive,
    TaskCompletionSource<OperationResult<TeamInfo?>> Reply) : TeamCommand;

/// <summary>添加允许路径 — 对应 AddTeamAllowedPathAsync</summary>
public sealed record AddAllowedPathCmd(
    string TeamId,
    string Path,
    AccessLevel AccessLevel,
    TaskCompletionSource<OperationResult<TeamInfo?>> Reply) : TeamCommand;
