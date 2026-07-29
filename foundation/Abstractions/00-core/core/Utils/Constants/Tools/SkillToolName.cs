namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 技能工具名称枚举
/// </summary>
public enum SkillToolName
{
    [EnumValue("Skill")] Skill,
    [EnumValue("skill_execute")] SkillExecute,
    [EnumValue("skill_list")] SkillList,
    [EnumValue("skill_simplify")] SkillSimplify,
    [EnumValue("skill_verify")] SkillVerify,
    [EnumValue("skill_debug")] SkillDebug,
    [EnumValue("skill_batch")] SkillBatch,
    [EnumValue("skill_stuck")] SkillStuck,
    [EnumValue("skill_search")] SkillSearch,
    [EnumValue("skill_recommend")] SkillRecommend,
    /// <summary>
    /// 技能发现 — 对齐 TS DiscoverSkillsTool
    /// 基于上下文自动发现相关技能
    /// </summary>
    [EnumValue("discover_skills")] DiscoverSkills,
}
