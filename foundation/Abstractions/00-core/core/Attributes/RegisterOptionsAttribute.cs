namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记 Options 类自动绑定到 Configuration — 源码生成器据此生成 BindConfiguration 调用
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RegisterOptionsAttribute : Attribute
{
    /// <summary>
    /// Configuration 路径（如 "Workflow:FileOperation"）
    /// </summary>
    public string? ConfigurationPath { get; set; }

    /// <summary>
    /// 是否在启动时验证
    /// </summary>
    public bool ValidateOnStart { get; set; }
}
