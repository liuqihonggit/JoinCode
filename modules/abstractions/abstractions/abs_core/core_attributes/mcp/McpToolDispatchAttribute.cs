namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class McpToolDispatchAttribute : Attribute {
    /// <summary>获取显示名称。</summary>
    public string DisplayName { get; }

    /// <summary>
    /// 当使用枚举构造函数时，存储枚举值供生成器提取 [EnumValue] 字符串
    /// </summary>
    public ToolCategory? CategoryEnum { get; }

    /// <summary>获取或设置是否可选。</summary>
    public bool Optional { get; set; }

    /// <summary>
    /// 工具类型 — 决定注入策略：System(始终注入+完整 schema) / Mcp(按分组注入，首次只发名称 — 两阶段加载) / OnError(报错时动态注入)
    /// 默认 Mcp（修复：默认 System 致 506 工具全量 schema 466KB 巨型请求被 10s 超时撞断 —
    /// 核心工具须显式声明 System，对齐基准 Claude Code 核心工具集 Read/Write/Edit/Grep/Glob/Bash/Task）
    /// </summary>
    public ToolKind Kind { get; set; } = ToolKind.Mcp;

    /// <summary>
    /// 二级分组名 — MCP工具必填，系统工具可选
    /// 同组工具在系统提示词中合并展示
    /// </summary>
    public string? GroupName { get; set; }

    /// <summary>构造 MCP 工具分发特性。</summary>
    /// <param name="displayName">显示名称。</param>
    public McpToolDispatchAttribute(string displayName) {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    /// <summary>构造 MCP 工具分发特性（按工具分类）。</summary>
    /// <param name="category">工具分类。</param>
    public McpToolDispatchAttribute(ToolCategory category) {
        CategoryEnum = category;
        DisplayName = category.ToString();
    }
}
