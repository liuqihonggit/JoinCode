
namespace JoinCode.Abstractions.Commands;

/// <summary>
/// 技能执行命令
/// </summary>
public sealed record SkillExecuteCommand(
    [Required(ErrorMessage = "skill_name 不能为空")]
    [StringLength(128, ErrorMessage = "技能名称过长")]
    string SkillName,
    Dictionary<string, JsonElement>? Parameters = null);

/// <summary>
/// 技能列表命令
/// </summary>
public sealed record SkillListCommand;
