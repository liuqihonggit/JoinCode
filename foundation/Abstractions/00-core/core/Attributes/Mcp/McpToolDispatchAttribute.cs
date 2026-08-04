namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class McpToolDispatchAttribute : Attribute
{
    public string DisplayName { get; }

    /// <summary>
    /// 当使用枚举构造函数时，存储枚举值供生成器提取 [EnumValue] 字符串
    /// </summary>
    public ToolCategory? CategoryEnum { get; }

    public bool Optional { get; set; }

    /// <summary>
    /// 工具类型 — 决定注入策略：System(始终注入) / Mcp(按分组注入) / OnError(报错时动态注入)
    /// 默认 System，保持向后兼容
    /// </summary>
    public ToolKind Kind { get; set; } = ToolKind.System;

    /// <summary>
    /// 二级分组名 — MCP工具必填，系统工具可选
    /// 同组工具在系统提示词中合并展示，>20组时用省略号
    /// </summary>
    public string? GroupName { get; set; }

    public McpToolDispatchAttribute(string displayName)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    public McpToolDispatchAttribute(ToolCategory category)
    {
        CategoryEnum = category;
        DisplayName = category.ToString();
    }
}
