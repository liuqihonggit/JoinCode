namespace Core.Utils;

/// <summary>
/// Agent 权限管理 Actor 命令类型 — 每个命令对应一个 IAgentPermissionManager 操作，由 PermissionActor Consumer 串行处理。
/// <para>TASK001: AsyncLock+文件 I/O 迁移到 Actor 邮箱管道，消除 5 处显式锁。</para>
/// <para>所有操作（含读操作 ListRulesAsync/GetMatchingRuleAsync）均经 Actor 串行化，因 EnsureRulesLoadedAsync 首次加载有副作用。</para>
/// </summary>
public abstract record AgentPermissionCommand;

/// <summary>添加权限规则 — 对应 AddRuleAsync</summary>
public sealed record AddRuleCmd(
    AgentPermissionRule Rule,
    TaskCompletionSource Reply) : AgentPermissionCommand;

/// <summary>移除权限规则 — 对应 RemoveRuleAsync</summary>
public sealed record RemoveRuleCmd(
    string AgentPattern,
    TaskCompletionSource<bool> Reply) : AgentPermissionCommand;

/// <summary>列出全部权限规则 — 对应 ListRulesAsync</summary>
public sealed record ListRulesCmd(
    TaskCompletionSource<IReadOnlyList<AgentPermissionRule>> Reply) : AgentPermissionCommand;

/// <summary>清空全部权限规则 — 对应 ClearRulesAsync</summary>
public sealed record ClearRulesCmd(
    TaskCompletionSource Reply) : AgentPermissionCommand;

/// <summary>获取匹配的权限规则 — 对应 GetMatchingRuleAsync</summary>
public sealed record GetMatchingRuleCmd(
    string AgentName,
    TaskCompletionSource<AgentPermissionRule?> Reply) : AgentPermissionCommand;