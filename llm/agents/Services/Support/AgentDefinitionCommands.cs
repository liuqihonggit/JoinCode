namespace Core.Agents;

/// <summary>
/// 代理定义加载 Actor 命令 — GetAgentDefinitionsAsync 的 Actor 化封装 — TASK001
/// <para>消除 AsyncLock + double-check 锁，改用 Actor 邮箱管道串行化加载操作。</para>
/// <para>Actor Consumer 单线程串行处理命令，天然保证缓存加载只执行一次，无需 double-check 锁。</para>
/// </summary>
public sealed record GetDefinitionsCmd(
    string? WorkingDirectory,
    TaskCompletionSource<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>> Reply);