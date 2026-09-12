namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记工具枚举成员的安全分类和权限模式 — 源码生成器据此自动生成工具安全集合
/// </summary>
/// <remarks>
/// 与 [EnumValue] 配合使用，[EnumValue] 提供工具名字符串，[SecurityClass] 提供安全分类
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SecurityClassAttribute : Attribute
{
    /// <summary>
    /// 安全分类 — 决定工具属于哪个安全集合
    /// 可选值: "readonly" / "safe-write" / "sensitive" / "destructive"
    /// </summary>
    public string Classification { get; }

    /// <summary>
    /// Auto 模式是否允许（默认 false）
    /// </summary>
    public bool AutoAllowed { get; init; }

    /// <summary>
    /// Plan 模式是否允许（默认 false）
    /// </summary>
    public bool PlanAllowed { get; init; }

    /// <summary>
    /// Ask 模式是否允许（默认 false）
    /// </summary>
    public bool AskAllowed { get; init; }

    /// <summary>
    /// Auto 模式是否显式拒绝（默认 false）
    /// </summary>
    public bool AutoDenied { get; init; }

    /// <summary>
    /// Plan 模式是否显式拒绝（默认 false）
    /// </summary>
    public bool PlanDenied { get; init; }

    /// <summary>
    /// Ask 模式是否显式拒绝（默认 false）
    /// </summary>
    public bool AskDenied { get; init; }

    /// <summary>
    /// Agent 视角下是否为破坏性工具（默认 false）
    /// </summary>
    public bool AgentDestructive { get; init; }

    public SecurityClassAttribute(string classification) => Classification = classification ?? throw new ArgumentNullException(nameof(classification));
}
