namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 子代理用户输入转发队列 — 独立于 IAgentMessageBroker，专用于用户在子代理运行期间追加的输入
/// 子代理在每轮 LLM 调用前通过 TryDrain 非阻塞消费队列中的用户转发消息
/// </summary>
public interface IAgentInputForwardQueue
{
    /// <summary>
    /// 注册子代理的输入转发队列（子代理启动时调用）
    /// </summary>
    void Register(string agentId);

    /// <summary>
    /// 注销子代理的输入转发队列（子代理结束时调用）
    /// </summary>
    void Unregister(string agentId);

    /// <summary>
    /// 向运行中的子代理追加用户输入（主代理转发时调用）
    /// </summary>
    Task EnqueueAsync(string agentId, string userInput, CancellationToken cancellationToken = default);

    /// <summary>
    /// 非阻塞 drain 队列中所有待处理的用户输入（子代理每轮 LLM 调用前调用）
    /// 返回所有已有消息但不等待新消息，队列为空返回空列表
    /// </summary>
    IReadOnlyList<string> TryDrain(string agentId);

    /// <summary>
    /// 检查指定子代理是否有待处理的用户输入
    /// </summary>
    bool HasPending(string agentId);
}
