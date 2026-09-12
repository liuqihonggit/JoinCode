namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记此类参与的 DI 循环依赖是合法的 — 编译期环检测器将豁免此类
/// 用于已知的、通过延迟解析打破的合法循环（如 TeamManager ↔ ITeammateObserver）
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AllowCycleAttribute : Attribute
{
    /// <summary>
    /// 允许循环的原因 — 必须说明为何此循环是安全的
    /// </summary>
    public string Reason { get; }

    public AllowCycleAttribute(string reason = "") => Reason = reason;
}
