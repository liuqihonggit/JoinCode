namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记枚举成员为子命令 — 源码生成器据此自动生成多级渐进式展开帮助文本
/// <para>用法: 标注在 <see cref="JoinCode.Abstractions.Utils.CliSubCommand"/> 枚举字段上</para>
/// <para>生成器扫描此特性, 生成 SubCommandHelpText 类, 支持按分类逐级展开</para>
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SubCommandInfoAttribute : Attribute {
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

    /// <summary>
    /// 是否自行处理 --help — 标记为 true 的子命令在 --help 时由子命令自身渲染帮助（如 gh pr view --help 动态生成工具参数），而非走全局帮助系统
    /// </summary>
    public bool SelfHelp { get; init; }

    /// <summary>
    /// 构造子命令信息特性。
    /// </summary>
    /// <param name="description">中文描述。</param>
    /// <param name="category">子命令分类。</param>
    public SubCommandInfoAttribute(string description, string category) {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Category = category ?? throw new ArgumentNullException(nameof(category));
    }
}
