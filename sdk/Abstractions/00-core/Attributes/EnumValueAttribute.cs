namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记枚举成员的字符串值 — 源码生成器据此生成 ToValue/FromValue 映射代码
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class EnumValueAttribute : Attribute
{
    /// <summary>
    /// 枚举成员对应的字符串值
    /// </summary>
    public string Value { get; }

    public EnumValueAttribute(string value) => Value = value;
}
