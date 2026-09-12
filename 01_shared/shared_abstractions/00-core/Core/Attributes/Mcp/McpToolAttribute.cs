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

    /// <summary>
    /// 工具类型 — 方法级可覆盖类级 Kind
    /// -1 表示未设置（继承类级），0=System, 1=Mcp, 2=OnError
    /// 使用 ToolKindConstants 常量赋值
    /// </summary>
    public int Kind { get; set; } = ToolKindConstants.Unset;

    /// <summary>
    /// 二级分组名 — 方法级可覆盖类级 GroupName
    /// 未设置时（null）继承类级 [McpToolDispatch(GroupName=...)]
    /// </summary>
    public string? GroupName { get; set; }

    public McpToolAttribute(string name, string description, string category = "other")
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Category = category ?? throw new ArgumentNullException(nameof(category));
    }
}

/// <summary>
/// McpToolAttribute.Kind 参数常量 — C# 特性不支持 nullable enum，使用 int + 常量
/// </summary>
public static class ToolKindConstants
{
    /// <summary>未设置 — 继承类级 Kind</summary>
    public const int Unset = -1;

    /// <summary>系统内置工具 — 始终注入系统提示词</summary>
    public const int System = 0;

    /// <summary>MCP远程工具 — 按分组注入</summary>
    public const int Mcp = 1;

    /// <summary>报错时动态注入</summary>
    public const int OnError = 2;
}
