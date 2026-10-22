namespace NativeTui.Core.Repl;

/// <summary>
/// 命令来源 — 对齐基准 commandSuggestions.ts 空查询分组视图的来源分类：
/// builtin（内置本地命令）/ user（用户级设置注入）/ project（项目级设置注入）/
/// policy（策略级设置注入）/ other（未知来源兜底）。枚举值序 = 空查询分组展示序
///（builtin → user → project → policy → other）。
/// </summary>
public enum CommandSource : byte
{
    /// <summary>内置命令（本地注册 — 基准 local/local-jsx 型）。</summary>
    Builtin = 0,

    /// <summary>用户命令（用户级设置注入 — 基准 userSettings/localSettings 型）。</summary>
    User = 1,

    /// <summary>项目命令（项目级设置注入 — 基准 projectSettings 型）。</summary>
    Project = 2,

    /// <summary>策略命令（策略级设置注入 — 基准 policySettings 型）。</summary>
    Policy = 3,

    /// <summary>其他来源（未知兜底 — 排序末位）。</summary>
    Other = 4,
}
