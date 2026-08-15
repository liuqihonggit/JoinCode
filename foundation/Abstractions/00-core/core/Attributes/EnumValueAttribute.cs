namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记枚举成员的字符串值 — 源码生成器据此生成 ToValue/FromValue 映射代码
/// 支持多个标注：第一个为主值（ToValue 返回），后续为别名（FromValue 也可匹配）
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
public sealed class EnumValueAttribute : Attribute
{
    /// <summary>
    /// 枚举成员对应的字符串值
    /// </summary>
    public string Value { get; }

    public EnumValueAttribute(string value) => Value = value;
}
