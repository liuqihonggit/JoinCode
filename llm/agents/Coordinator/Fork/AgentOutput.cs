namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 输出值对象 — 可变运行时累计，记录执行过程中的消耗与产出
/// 包含已用 Token、完成轮数、执行输出文本、错误消息、路由列表
/// </summary>
public sealed class AgentOutput
{
    /// <summary>已使用 Token 数</summary>
    public int TokensUsed { get; set; }

    /// <summary>已完成的对话轮数</summary>
    public int TurnsCompleted { get; set; }

    /// <summary>执行输出文本</summary>
    public string? Text { get; set; }

    /// <summary>错误消息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>路由列表</summary>
    public string[]? Routes { get; set; }
}
