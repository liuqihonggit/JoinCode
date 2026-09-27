namespace Core.Agents;

/// <summary>
/// 代理定义加载 Actor 命令 — GetAgentDefinitionsAsync 的 Actor 化封装 — TASK001
/// <para>消除 AsyncLock + double-check 锁，改用 Actor 邮箱管道串行化加载操作。</para>
/// <para>Actor Consumer 单线程串行处理命令，天然保证缓存加载只执行一次，无需 double-check 锁。</para>
/// </summary>
public sealed record GetDefinitionsCmd(
    string? WorkingDirectory,
    IdempotencyKey IdempotencyKey) : IRequestCommand {
    /// <summary>回复通道 — Consumer 处理完成后写入结果，调用方通过 Reader.ReadAsync 拉取</summary>
    public Channel<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>> ReplyChannel { get; } = Channel.CreateUnbounded<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>>();

    /// <summary>从幂等缓存恢复结果 — 命中缓存时写入 ReplyChannel 并返回 true</summary>
    public bool TryRestoreFromCache(IIdempotencyStore store) {
        if (store.TryGetResult<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>>(IdempotencyKey, out var cached)) {
            ReplyChannel.Writer.TryWrite(cached!);
            return true;
        }
        return false;
    }
}