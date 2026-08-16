namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 技能工具名称枚举
/// </summary>
public enum SkillToolName
{
    [EnumValue("Skill")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    Skill,

    [EnumValue("skill_execute")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    SkillExecute,

    [EnumValue("skill_list")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SkillList,

    [EnumValue("skill_simplify")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    SkillSimplify,

    [EnumValue("skill_verify")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SkillVerify,

    [EnumValue("skill_debug")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    SkillDebug,

    [EnumValue("skill_batch")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    SkillBatch,

    [EnumValue("skill_stuck")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    SkillStuck,

    [EnumValue("skill_search")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SkillSearch,

    [EnumValue("skill_recommend")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SkillRecommend,

    /// <summary>
    /// 技能发现 — 对齐 TS DiscoverSkillsTool
    /// 基于上下文自动发现相关技能
    /// </summary>
    [EnumValue("discover_skills")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    DiscoverSkills,
}
