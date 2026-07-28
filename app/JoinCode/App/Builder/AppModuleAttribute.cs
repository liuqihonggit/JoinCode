namespace JoinCode.App.Builder;

/// <summary>
/// 标记类为应用模块 — 源码生成器据此自动生成 ApplicationBuilder.UseModule 链式调用代码
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AppModuleAttribute : Attribute
{
    /// <summary>
    /// 模块执行优先级 — 数值越小越先执行，默认 100
    /// </summary>
    public int Order { get; set; } = 100;
}
