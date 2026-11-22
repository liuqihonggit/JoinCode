namespace JoinCode.Abstractions.Tools;

/// <summary>
/// CLI 工具的参数描述 — 来自 node.yaml / base.yaml 的单一数据源。
/// 取代 [McpToolParameter] 特性，去掉源码生成器后由 YAML 独立持有。
/// </summary>
public sealed record CliToolParameter {
    /// <summary>参数名（CLI 长选项名，snake_case）。</summary>
    public required string Name { get; init; }

    /// <summary>参数类型：string / integer / boolean / array。</summary>
    public required string Type { get; init; }

    /// <summary>参数说明 — 只在 -h 输出中出现，永不注入 LLM 提示词。</summary>
    public string? Description { get; init; }

    /// <summary>是否必填。</summary>
    public bool Required { get; init; }

    /// <summary>默认值（字符串形式）。</summary>
    public string? Default { get; init; }

    /// <summary>短别名，逗号分隔（如 "-n,--line-number"）。</summary>
    public string? Aliases { get; init; }

    /// <summary>枚举候选值，逗号分隔。</summary>
    public string? Enum { get; init; }

    /// <summary>
    /// 解析时 raw YAML 中显式出现过的键名 — 供继承合并判断"省略 vs 显式声明"。
    /// Type/Required 有默认值，不能靠 null 区分是否显式，必须靠这个集合。
    /// 手工 new 的参数集合为空，视为全部字段显式。
    /// </summary>
    internal IReadOnlySet<string> DeclaredKeys { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}
