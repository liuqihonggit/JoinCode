
namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记枚举成员的模型元数据 — 源码生成器据此按 Provider 分组生成 ModelEntry[] 数组
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class ModelInfoAttribute : Attribute
{
    /// <summary>
    /// 所属 Provider 类型
    /// </summary>
    public string Provider { get; }

    /// <summary>
    /// 显示名称（如 "GPT-4o"）
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 上下文窗口大小（token 数）
    /// </summary>
    public int ContextWindow { get; }

    /// <summary>
    /// 模型描述
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 是否为该 Provider 的默认模型
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// 是否为该 Provider 的快速模型
    /// </summary>
    public bool IsFastDefault { get; init; }

    public ModelInfoAttribute(string provider, string displayName, int contextWindow, string description = "")
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        ContextWindow = contextWindow;
        Description = description ?? string.Empty;
    }
}
