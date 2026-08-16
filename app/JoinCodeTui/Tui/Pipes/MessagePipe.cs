namespace JoinCode.Tui.Pipes;

/// <summary>
/// 消息管道实现 — 每个 Agent 拥有独立的消息队列。
/// 线程安全：使用 ConcurrentQueue 存储消息，volatile 保证状态可见性。
/// 消息上限 1000 条，超过时自动移除最旧消息。
/// </summary>
public sealed class MessagePipe : IMessagePipe
{
    private readonly ConcurrentQueue<TuiMessage> _messages = new();
    private volatile AgentState _state = AgentState.Waiting;
    private const int MaxMessages = 1000;

    /// <inheritdoc />
    public string AgentId { get; }

    /// <inheritdoc />
    public string AgentName { get; }

    /// <inheritdoc />
    public AgentState State => _state;

    /// <inheritdoc />
    public bool IsMain { get; }

    /// <inheritdoc />
    public int MessageCount => _messages.Count;

    /// <inheritdoc />
    public IReadOnlyList<TuiMessage> Messages => [.. _messages];

    /// <summary>创建消息管道。</summary>
    /// <param name="agentId">Agent 唯一标识。</param>
    /// <param name="agentName">Agent 显示名称。</param>
    /// <param name="isMain">是否为主 Agent。</param>
    public MessagePipe(string agentId, string agentName, bool isMain = false)
    {
        AgentId = agentId;
        AgentName = agentName;
        IsMain = isMain;
        _state = isMain ? AgentState.Running : AgentState.Waiting;
    }

    /// <inheritdoc />
    public IReadOnlyList<TuiMessage> GetNewMessages(DateTime since)
    {
        return [.. _messages.Where(m => m.Timestamp > since)];
    }

    /// <inheritdoc />
    public void AddMessage(TuiMessage message)
    {
        _messages.Enqueue(message);
        TrimExcess();
    }

    /// <inheritdoc />
    public void UpdateState(AgentState state)
    {
        _state = state;
    }

    /// <inheritdoc />
    public void Clear()
    {
        while (_messages.TryDequeue(out _)) { }
    }

    private void TrimExcess()
    {
        while (_messages.Count > MaxMessages && _messages.TryDequeue(out _)) { }
    }
}
