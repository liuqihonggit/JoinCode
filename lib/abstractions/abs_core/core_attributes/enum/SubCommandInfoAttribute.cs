namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记枚举成员为子命令 — 源码生成器据此自动生成多级渐进式展开帮助文本
/// <para>用法: 标注在 <see cref="JoinCode.Abstractions.Utils.CliSubCommand"/> 枚举字段上</para>
/// <para>生成器扫描此特性, 生成 SubCommandHelpText 类, 支持按分类逐级展开</para>
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SubCommandInfoAttribute : Attribute
{
    /// <summary>中文描述</summary>
    public string Description { get; }

    /// <summary>
    /// 子命令分类 — 生成器按分类分组输出帮助文本
    /// 如 "MCP 工具"、"斜杠命令"、"诊断"、"搜索"、"GitHub"
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// 使用示例 — 如 "jcc mcp_call gh_pr_view {\"pr_number\":\"123\"}"
    /// </summary>
    public string? Example { get; init; }

    /// <summary>
    /// 是否为别名 — 别名不单独显示, 跟随主命令
    /// </summary>
    public bool IsAlias { get; init; }

    /// <summary>
    /// 别名目标 — 如 rc/remote 的 AliasOf="remote-control"
    /// </summary>
    public string? AliasOf { get; init; }

    /// <summary>
    /// 是否已废弃 — 废弃命令单独归类, 提示替代方案
    /// </summary>
    public bool IsDeprecated { get; init; }

    public SubCommandInfoAttribute(string description, string category)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Category = category ?? throw new ArgumentNullException(nameof(category));
    }
}
