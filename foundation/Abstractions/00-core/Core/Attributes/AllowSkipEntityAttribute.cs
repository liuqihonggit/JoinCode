namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记此 [Register] 类豁免强制继承 ServiceEntity/Entity — 用于已有其他基类的类（C# 单继承冲突）
/// 必须在注释中说明豁免原因
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AllowSkipEntityAttribute : Attribute
{
    /// <summary>
    /// 豁免原因 — 必须说明为何此类不能继承 ServiceEntity
    /// </summary>
    public string Reason { get; }

    public AllowSkipEntityAttribute(string reason = "") => Reason = reason;
}
