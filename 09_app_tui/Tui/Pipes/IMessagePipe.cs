namespace JoinCode.Tui.Pipes;

/// <summary>
/// 消息管道 — 每个 Agent 拥有独立的消息队列，UI 通过轮询拉取。
/// 管道隔离保证各 Agent 消息互不干扰，统一渲染时按时间戳排序。
/// </summary>
public interface IMessagePipe
{
    /// <summary>Agent 唯一标识。</summary>
    string AgentId { get; }

    /// <summary>Agent 显示名称。</summary>
    string AgentName { get; }

    /// <summary>Agent 当前运行状态。</summary>
    AgentState State { get; }

    /// <summary>是否为主 Agent（false 表示子 Agent）。</summary>
    bool IsMain { get; }

    /// <summary>管道中消息总数。</summary>
    int MessageCount { get; }

    /// <summary>获取所有消息（只读快照）。</summary>
    IReadOnlyList<TuiMessage> Messages { get; }

    /// <summary>获取指定时间之后的新消息（用于轮询增量拉取）。</summary>
    IReadOnlyList<TuiMessage> GetNewMessages(DateTime since);

    /// <summary>添加消息到管道。</summary>
    void AddMessage(TuiMessage message);

    /// <summary>更新 Agent 状态。</summary>
    void UpdateState(AgentState state);

    /// <summary>清空管道中所有消息。</summary>
    void Clear();
}
