
namespace Core.Skills.Discovery;

/// <summary>
/// 已发现的技能记录 — 描述技能文件加载后的元信息和验证状态
/// </summary>
public sealed record DiscoveredSkill {
    /// <summary>
    /// 技能名称
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// 源文件路径
    /// </summary>
    public required string SourcePath { get; init; }
    /// <summary>
    /// 源文件格式（JSON 或 Markdown）
    /// </summary>
    public required SkillSourceFormat SourceFormat { get; init; }
    /// <summary>
    /// 最后修改时间
    /// </summary>
    public required DateTime LastModified { get; init; }
    /// <summary>
    /// 技能定义
    /// </summary>
    public required SkillDefinition Definition { get; init; }
    /// <summary>
    /// 验证错误列表
    /// </summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
    /// <summary>
    /// 验证警告列表
    /// </summary>
    public IReadOnlyList<string> ValidationWarnings { get; init; } = Array.Empty<string>();
    /// <summary>
    /// 是否验证通过（无错误）
    /// </summary>
    public bool IsValid => ValidationErrors.Count == 0;
}