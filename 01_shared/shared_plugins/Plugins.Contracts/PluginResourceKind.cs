namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 插件资源类型 — 细粒度资源分类
/// <para>每个命令/钩子/技能/Agent 都是一个 Resource,引用计数精确到单个资源</para>
/// </summary>
public enum PluginResourceKind
{
    [EnumValue("command")] Command,
    [EnumValue("hook")] Hook,
    [EnumValue("skill")] Skill,
    [EnumValue("agent")] Agent,
}
