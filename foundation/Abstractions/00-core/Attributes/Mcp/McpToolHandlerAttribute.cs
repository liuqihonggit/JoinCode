namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class McpToolHandlerAttribute : Attribute
{
    public string DisplayName { get; }

    /// <summary>
    /// 当使用枚举构造函数时，存储枚举值供生成器提取 [EnumValue] 字符串
    /// </summary>
    public ToolCategory? CategoryEnum { get; }

    public bool Optional { get; set; }

    public McpToolHandlerAttribute(string displayName)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    public McpToolHandlerAttribute(ToolCategory category)
    {
        CategoryEnum = category;
        DisplayName = category.ToString();
    }
}
