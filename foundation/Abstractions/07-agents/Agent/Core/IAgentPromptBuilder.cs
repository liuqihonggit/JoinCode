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
}
