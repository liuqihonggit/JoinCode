namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记方法参数为 MCP 工具选项容器。
/// 源码生成器会将该参数类型的 [McpToolParameter] 属性展开为工具参数，
/// 并在调用时自动构造选项对象。
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class McpToolOptionsAttribute : Attribute
{
}
