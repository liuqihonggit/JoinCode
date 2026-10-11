
namespace Core.Skills.Discovery;

/// <summary>
/// 技能发现服务接口 — 扫描技能目录、加载技能定义、监控文件变更
/// </summary>
public interface ISkillDiscoveryService : IAsyncDisposable {
    /// <summary>
    /// 异步发现所有技能
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已发现的技能列表</returns>
    Task<IReadOnlyList<DiscoveredSkill>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按技能名异步加载单个技能
    /// </summary>
    /// <param name="skillName">技能名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>发现的技能；不存在则返回 null</returns>
    Task<DiscoveredSkill?> LoadSkillAsync(string skillName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步验证技能文件
    /// </summary>
    /// <param name="filePath">技能文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验证结果</returns>
    Task<SkillValidationResult> ValidateSkillAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动技能目录监控
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task StartWatchingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止技能目录监控
    /// </summary>
    void StopWatching();

    /// <summary>
    /// 发现新技能事件
    /// </summary>
    event EventHandler<SkillDiscoveredEventArgs>? SkillDiscovered;

    /// <summary>
    /// 技能变更事件
    /// </summary>
    event EventHandler<SkillChangedEventArgs>? SkillChanged;

    /// <summary>
    /// 技能移除事件
    /// </summary>
    event EventHandler<SkillRemovedEventArgs>? SkillRemoved;
}

/// <summary>
/// 技能发现事件参数
/// </summary>
public sealed class SkillDiscoveredEventArgs : EventArgs {
    /// <summary>
    /// 已发现的技能
    /// </summary>
    public required DiscoveredSkill Skill { get; init; }
}

/// <summary>
/// 技能变更事件参数
/// </summary>
public sealed class SkillChangedEventArgs : EventArgs {
    /// <summary>
    /// 变更后的技能
    /// </summary>
    public required DiscoveredSkill Skill { get; init; }
}

/// <summary>
/// 技能移除事件参数
/// </summary>
public sealed class SkillRemovedEventArgs : EventArgs {
    /// <summary>
    /// 被移除的技能名称
    /// </summary>
    public required string SkillName { get; init; }
    /// <summary>
    /// 被移除技能的源文件路径
    /// </summary>
    public required string SourcePath { get; init; }
}

/// <summary>
/// 技能验证结果
/// </summary>
public sealed class SkillValidationResult {
    /// <summary>
    /// 文件路径
    /// </summary>
    public required string FilePath { get; init; }
    /// <summary>
    /// 是否验证通过
    /// </summary>
    public bool IsValid { get; init; }
    /// <summary>
    /// 验证错误列表
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    /// <summary>
    /// 验证警告列表
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    /// <summary>
    /// 解析得到的技能定义；验证失败时可能为 null
    /// </summary>
    public SkillDefinition? SkillDefinition { get; init; }

    /// <summary>
    /// 创建验证成功结果
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="definition">技能定义</param>
    /// <param name="warnings">警告列表</param>
    /// <returns>验证成功结果</returns>
    public static SkillValidationResult Success(string filePath, SkillDefinition definition, IReadOnlyList<string>? warnings = null)
        => new() {
            FilePath = filePath,
            IsValid = true,
            SkillDefinition = definition,
            Warnings = warnings ?? Array.Empty<string>()
        };

    /// <summary>
    /// 创建验证失败结果
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="errors">错误列表</param>
    /// <param name="warnings">警告列表</param>
    /// <returns>验证失败结果</returns>
    public static SkillValidationResult Failure(string filePath, IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null)
        => new() {
            FilePath = filePath,
            IsValid = false,
            Errors = errors,
            Warnings = warnings ?? Array.Empty<string>()
        };
}