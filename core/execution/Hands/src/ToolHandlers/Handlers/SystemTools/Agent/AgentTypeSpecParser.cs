namespace Tools.Handlers;

/// <summary>
/// 解析 Agent(worker,researcher) 语法 — 限制可 spawn 的 agent 类型
/// <para>对齐 TS 原版 resolveAgentTools 的 allowedAgentTypes 元数据</para>
/// <para>"worker,researcher" → AllowedTypes=["worker","researcher"], PrimaryType="worker"</para>
/// </summary>
public static class AgentTypeSpecParser
{
    /// <summary>解析逗号分隔的 agent 类型语法,返回主类型和允许类型列表</summary>
    public static (string PrimaryType, IReadOnlyList<string>? AllowedTypes) Parse(string? subagentType)
    {
        if (string.IsNullOrWhiteSpace(subagentType))
            return (string.Empty, null);

        var trimmed = subagentType.Trim();

        if (!trimmed.Contains(','))
            return (trimmed, null);

        var parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return (string.Empty, null);

        return (parts[0], parts);
    }

    /// <summary>检查请求的 agent 类型是否在允许列表中</summary>
    public static bool IsAllowed(string requestedType, IReadOnlyList<string>? allowedTypes)
    {
        if (allowedTypes is null or { Count: 0 })
            return true;

        return allowedTypes.Any(t => string.Equals(t, requestedType, StringComparison.OrdinalIgnoreCase));
    }
}
