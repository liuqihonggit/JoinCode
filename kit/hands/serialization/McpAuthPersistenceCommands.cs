namespace IO.Services;

/// <summary>
/// MCP 认证持久化 Actor 命令类型 — 每个命令对应一个 IMcpAuthPersistenceService 操作，由 Actor Consumer 串行处理。
/// <para>ADR 0115: AsyncLock 迁移到 Actor 邮箱管道，消除显式锁。</para>
/// </summary>
public abstract record McpAuthPersistenceCommand;

/// <summary>保存认证条目 — 对应 SaveAsync</summary>
public sealed record SaveAuthCmd(
    string AuthName,
    string AuthType,
    string SerializedData,
    TaskCompletionSource Reply) : McpAuthPersistenceCommand;

/// <summary>加载认证条目 — 对应 LoadAsync</summary>
public sealed record LoadAuthCmd(
    string AuthName,
    TaskCompletionSource<AuthConfigEntry?> Reply) : McpAuthPersistenceCommand;

/// <summary>列出全部认证条目 — 对应 ListAsync</summary>
public sealed record ListAuthCmd(
    TaskCompletionSource<IReadOnlyList<AuthConfigEntry>> Reply) : McpAuthPersistenceCommand;

/// <summary>移除认证条目 — 对应 RemoveAsync</summary>
public sealed record RemoveAuthCmd(
    string AuthName,
    TaskCompletionSource Reply) : McpAuthPersistenceCommand;
