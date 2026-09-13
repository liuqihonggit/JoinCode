
namespace Core.Skills.Mcp;

/// <summary>
/// MCP 技能提供者接口 — 管理来自 MCP 服务器的远程技能
/// </summary>
public interface IMcpSkillProvider : IAsyncDisposable
{
    /// <summary>
    /// 异步获取所有 MCP 技能定义
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>MCP 技能定义列表</returns>
    Task<IReadOnlyList<SkillDefinition>> GetMcpSkillsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步执行 MCP 远程技能
    /// </summary>
    /// <param name="skillName">技能名称</param>
    /// <param name="parameters">调用参数</param>
    /// <param name="ctx">执行上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>技能执行结果</returns>
    Task<SkillResult> ExecuteMcpSkillAsync(
        string skillName,
        Dictionary<string, JsonElement>? parameters,
        ExecutionContext ctx,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步刷新所有 MCP 服务器的技能列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断指定名称的 MCP 技能是否可用
    /// </summary>
    /// <param name="skillName">技能名称</param>
    /// <returns>可用返回 true，否则返回 false</returns>
    bool IsSkillAvailable(string skillName);
}
