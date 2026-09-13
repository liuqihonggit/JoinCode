namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 代理提示词构建器 - 基于 AgentDefinition 构建系统提示词
/// </summary>
public interface IAgentPromptBuilder
{
    /// <summary>
    /// 构建 SubAgent 系统提示词
    /// </summary>
    /// <param name="agentType">代理类型</param>
    /// <param name="task">任务描述</param>
    /// <param name="context">上下文信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>系统提示词</returns>
    Task<string> BuildSystemPromptAsync(
        string? agentType,
        string task,
        IReadOnlyList<string>? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 构建 SubAgent 系统提示词 — 带运行时上下文,供内置 agent 动态注入当前 MCP/skills/settings
    /// <para>对齐 TS 原版 getSystemPrompt({ toolUseContext }) 闭包模式</para>
    /// <para>当 <paramref name="promptContext"/> 为 null 时,行为与无上下文重载一致</para>
    /// </summary>
    /// <param name="agentType">代理类型</param>
    /// <param name="task">任务描述</param>
    /// <param name="context">上下文信息</param>
    /// <param name="promptContext">运行时提示词上下文(当前 MCP/skills/settings)</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>系统提示词</returns>
    Task<string> BuildSystemPromptAsync(
        string? agentType,
        string task,
        IReadOnlyList<string>? context,
        AgentPromptContext? promptContext,
        CancellationToken cancellationToken = default);
}
