namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class McpToolParameterAttribute : Attribute {
    /// <summary>获取参数描述。</summary>
    public string Description { get; }
    /// <summary>获取或设置是否必填。</summary>
    public bool Required { get; set; } = true;
    /// <summary>获取或设置默认值。</summary>
    public string? DefaultValue { get; set; }
    /// <summary>获取或设置枚举值列表。</summary>
    public string[]? EnumValues { get; set; }

    /// <summary>构造 MCP 工具参数特性。</summary>
    public McpToolParameterAttribute(string description) {
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }
}