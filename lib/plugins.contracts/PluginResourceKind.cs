namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 插件资源类型 — 细粒度资源分类
/// <para>每个命令/钩子/技能/Agent 都是一个 Resource,引用计数精确到单个资源</para>
/// </summary>
public enum PluginResourceKind
{
    /// <summary>
    /// 命令资源
    /// </summary>
    [EnumValue("command")] Command,

    /// <summary>
    /// 钩子资源
    /// </summary>
    [EnumValue("hook")] Hook,

    /// <summary>
    /// 技能资源
    /// </summary>
    [EnumValue("skill")] Skill,

    /// <summary>
    /// 智能体资源
    /// </summary>
    [EnumValue("agent")] Agent,
}
