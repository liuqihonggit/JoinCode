namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 内置 Agent 提示词特性 — 标记在 static class 上，声明该类提供指定 Agent 类型的系统提示词
/// 源码生成器扫描此特性，自动生成 AgentPromptRegistration 注册代码
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AgentPromptAttribute : Attribute
{
    /// <summary>
    /// Agent 类型名称（对应 BuiltInAgentType 枚举的 [EnumValue] 值）
    /// </summary>
    public required string AgentType { get; init; }

    /// <summary>
    /// Agent 显示名称
    /// </summary>
    public string DisplayName { get; init; } = "";

    /// <summary>
    /// Agent 描述
    /// </summary>
    public string Description { get; init; } = "";
}
