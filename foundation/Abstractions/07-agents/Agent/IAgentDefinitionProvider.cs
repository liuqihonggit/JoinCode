namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 代理定义提供者 - 从多个来源加载代理定义
/// </summary>
public interface IAgentDefinitionProvider
{
    /// <summary>
    /// 获取所有可用的代理定义
    /// </summary>
    /// <param name="workingDirectory">工作目录（用于加载项目级代理定义）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>代理定义列表</returns>
    Task<List<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition>> GetAgentDefinitionsAsync(string? workingDirectory = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定角色的代理定义
    /// </summary>
    Task<JoinCode.Abstractions.Prompts.ToolPrompts.AgentDefinition?> GetAgentDefinitionAsync(AgentRole role, ExecutorVariant? variant = null, string? workingDirectory = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除代理定义缓存 — 对齐 TS: clearAgentDefinitionsCache
    /// </summary>
    void ClearCache();
}
