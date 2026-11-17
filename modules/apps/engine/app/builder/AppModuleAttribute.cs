namespace JoinCode.App.Builder;

/// <summary>
/// 标记类为应用模块 — 源码生成器据此自动生成 ApplicationBuilder.UseModule 链式调用代码
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AppModuleAttribute : Attribute {
}