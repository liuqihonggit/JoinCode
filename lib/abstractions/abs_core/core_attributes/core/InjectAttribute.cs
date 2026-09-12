namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记字段由源码生成器自动注入 — 生成器据此生成构造函数参数和赋值代码
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class InjectAttribute : Attribute
{
}
