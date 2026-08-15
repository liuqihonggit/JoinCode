namespace Core.Agents.Coordinator.Core.Messaging;

/// <summary>
/// Agent 输出 channel 管理器实现 — 单一汇聚 Channel&lt;AgentOutputChunk&gt;
/// 所有 Agent 的流式输出写入同一个 channel，前台通过 ReadAllAsync 拉取
/// /switch 命令通过 AgentOutputDisplayMode 过滤，不匹配的 chunk 跳过不显示
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager))]
public sealed partial class AgentOutputChannelManager : ServiceEntity, JoinCode.Abstractions.Interfaces.IAgentOutputChannelManager
{
    private readonly System.Threading.Channels.Channel<JoinCode.Abstractions.Interfaces.AgentOutputChunk> _outputChannel =
        System.Threading.Channels.Channel.CreateUnbounded<JoinCode.Abstractions.Interfaces.AgentOutputChunk>();
    private readonly ConcurrentDictionary<string, string?> _activeAgents = new();
    private readonly ILogger? _logger;

    public AgentOutputChannelManager(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 注册 Agent
    /// </summary>
    public void Register(string agentId, string? displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        _activeAgents[agentId] = displayName;
    }

    /// <summary>
    /// 注销 Agent
    /// </summary>
    public void Unregister(string agentId)
    {
        _activeAgents.TryRemove(agentId, out _);
    }

    /// <summary>
    /// 向输出 channel 写入 chunk
    /// </summary>
    public void Write(string agentId, string? agentName, string content, JoinCode.Abstractions.Interfaces.AgentOutputChunkType type)
    {
        if (string.IsNullOrEmpty(content)) return;
        _outputChannel.Writer.TryWrite(new JoinCode.Abstractions.Interfaces.AgentOutputChunk
        {
            AgentId = agentId,
            AgentName = agentName,
            Content = content,
            Type = type
        });
    }

    /// <summary>
    /// 从输出 channel 拉取所有 chunk
    /// </summary>
    public async IAsyncEnumerable<JoinCode.Abstractions.Interfaces.AgentOutputChunk> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var chunk in _outputChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 获取所有活跃 Agent 列表
    /// </summary>
    public IReadOnlyList<JoinCode.Abstractions.Interfaces.AgentOutputInfo> GetActiveAgents()
    {
        return _activeAgents.Select(kv => new JoinCode.Abstractions.Interfaces.AgentOutputInfo
        {
            AgentId = kv.Key,
            DisplayName = kv.Value
        }).ToList();
    }
}
