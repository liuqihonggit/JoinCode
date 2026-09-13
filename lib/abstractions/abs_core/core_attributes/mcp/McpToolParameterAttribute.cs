namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class McpToolParameterAttribute : Attribute
{
    public string Description { get; }
    public bool Required { get; set; } = true;
    public string? DefaultValue { get; set; }
    public string[]? EnumValues { get; set; }

    public McpToolParameterAttribute(string description)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }
}
