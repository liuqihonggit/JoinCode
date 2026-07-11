
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 技能服务接口，提供技能执行和管理功能
/// </summary>
public interface ISkillService : IDisposable
{
    /// <summary>
    /// 执行技能
    /// </summary>
    Task<SkillResult> ExecuteAsync(
        string skillName,
        Dictionary<string, JsonElement>? parameters,
        SkillExecutionContext ctx,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有可用技能
    /// </summary>
    Task<IReadOnlyList<SkillDefinition>> GetAvailableSkillsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取技能定义
    /// </summary>
    Task<SkillDefinition?> GetSkillAsync(string skillName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查技能是否存在
    /// </summary>
    bool SkillExists(string skillName);

    /// <summary>
    /// 重新加载技能
    /// </summary>
    Task<bool> ReloadAsync(string? skillName, SkillExecutionContext ctx, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册技能
    /// </summary>
    void RegisterSkill(SkillDefinition skill);

    /// <summary>
    /// 注销技能
    /// </summary>
    bool UnregisterSkill(string skillName);
}

/// <summary>
/// 技能执行上下文
/// </summary>
public sealed record SkillExecutionContext(
    CancellationToken CancellationToken = default,
    ILogger? Logger = null
);
