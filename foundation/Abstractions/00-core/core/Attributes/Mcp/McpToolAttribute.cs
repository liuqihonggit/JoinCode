namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public string Category { get; }

    public McpToolAttribute(string name, string description, string category = "other")
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Category = category ?? throw new ArgumentNullException(nameof(category));
    }
}
