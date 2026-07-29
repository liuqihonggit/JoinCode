namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 工具提示词分类
/// </summary>
public enum ToolPromptCategory
{
    Agent,
    File,
    Planning,
    Search,
    Shell,
    System
}

/// <summary>
/// 工具提示词特性 — 标记在 static class 上，声明工具的详细描述
/// 源码生成器扫描此特性，自动生成 ToolPromptRegistration 注册代码
/// 详细描述在 API 请求构建时替换 [McpTool] 的短描述，对齐 TS 版本 tool.prompt() 的行为
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ToolPromptAttribute : Attribute
{
    /// <summary>
    /// 工具名称（对应 ToolNameConstants 中的常量值）
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// 工具分类
    /// </summary>
    public ToolPromptCategory Category { get; init; }

    /// <summary>
    /// 是否需要运行时参数（如 agentSwarmsEnabled）
    /// 为 true 时，生成器生成带参数的注册方法
    /// </summary>
    public bool HasParameters { get; init; }
}
