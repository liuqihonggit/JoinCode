namespace JoinCode.Entry;

/// <summary>
/// Agent 输出显示模式 — /switch 命令设置，前台显示 task 读取
/// null = 显示全部 Agent 输出（默认）
/// 非 null = 只显示指定 AgentId 的输出
/// </summary>
internal static class AgentOutputDisplayMode
{
    private static volatile string? _targetAgentId;

    /// <summary>
    /// 目标 Agent ID — null 表示显示全部
    /// </summary>
    internal static string? TargetAgentId
    {
        get => _targetAgentId;
        set => _targetAgentId = value;
    }

    /// <summary>
    /// 是否显示指定 Agent 的输出
    /// </summary>
    internal static bool ShouldDisplay(string agentId)
    {
        var target = _targetAgentId;
        return target is null || string.Equals(target, agentId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 重置为显示全部
    /// </summary>
    internal static void Reset()
    {
        _targetAgentId = null;
    }
}
