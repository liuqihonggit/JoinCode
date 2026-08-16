namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Agent 提示词构建上下文 — 携带运行时配置,供内置 agent 动态注入
/// <para>对齐 claude code getSystemPrompt({ toolUseContext }) 闭包模式</para>
/// <para>当 agent 需要感知当前 MCP 服务器、可用 skills、settings 时,通过此上下文注入</para>
/// </summary>
public sealed record AgentPromptContext
{
    /// <summary>当前已配置的 MCP 服务器名称列表 — 供 GuideAgent 等注入到 prompt</summary>
    public IReadOnlyList<string>? McpServers { get; init; }

    /// <summary>当前可用的 skill 名称列表 — 供 GuideAgent 等注入到 prompt</summary>
    public IReadOnlyList<string>? AvailableSkills { get; init; }

    /// <summary>当前 settings 摘要(如权限模式、模型配置等) — 供 GuideAgent 等注入到 prompt</summary>
    public string? SettingsSummary { get; init; }
}
