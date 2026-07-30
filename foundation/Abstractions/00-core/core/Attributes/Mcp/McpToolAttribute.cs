namespace JoinCode.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public string Category { get; }

    /// <summary>
    /// 标记此工具为并发安全 — 可与其他并发安全工具并行执行
    /// 对齐 TS isConcurrencySafe()，源码生成器据此生成 ToolConcurrencyCache.SafeTools 集合
    /// </summary>
    public bool ConcurrencySafe { get; set; }

    public McpToolAttribute(string name, string description, string category = "other")
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Category = category ?? throw new ArgumentNullException(nameof(category));
    }
}
