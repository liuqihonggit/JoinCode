namespace JoinCode.Entry;

/// <summary>
/// @agentName 语法解析器 — 用户用 @指定子代理直接注入输入，绕过主代理 LLM 协同决策
/// 语法: @agentName 消息内容
/// 匹配优先级: DisplayName 精确 → Description 精确 → Id 前缀
/// </summary>
internal static class SubAgentMentionParser
{
    /// <summary>
    /// 尝试解析 @agentName 消息 语法
    /// </summary>
    /// <returns>(agentName, message) 或 null（非 @ 语法或格式无效）</returns>
    internal static (string AgentName, string Message)? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input[0] != '@') return null;

        var spaceIndex = input.IndexOf(' ');
        if (spaceIndex < 0) return null;

        var agentName = input[1..spaceIndex];
        var message = input[(spaceIndex + 1)..];

        if (string.IsNullOrWhiteSpace(agentName) || string.IsNullOrWhiteSpace(message)) return null;

        return (agentName, message.Trim());
    }

    /// <summary>
    /// 按名称模糊匹配运行中子代理
    /// 匹配优先级: DisplayName 精确 → Description 精确 → Id 前缀
    /// </summary>
    internal static JoinCode.Abstractions.Interfaces.RunningAgentInfo? FindAgent(
        string agentName,
        IEnumerable<JoinCode.Abstractions.Interfaces.RunningAgentInfo> runningAgents)
    {
        var agents = runningAgents as IReadOnlyCollection<JoinCode.Abstractions.Interfaces.RunningAgentInfo> ?? runningAgents.ToList();
        if (agents.Count == 0) return null;

        return agents.FirstOrDefault(a =>
            string.Equals(a.DisplayName, agentName, StringComparison.OrdinalIgnoreCase))
            ?? agents.FirstOrDefault(a =>
            string.Equals(a.Description, agentName, StringComparison.OrdinalIgnoreCase))
            ?? agents.FirstOrDefault(a =>
            a.Id.StartsWith(agentName, StringComparison.OrdinalIgnoreCase));
    }
}
