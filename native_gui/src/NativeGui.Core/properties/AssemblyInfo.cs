// Core internal 面向宿主与测试开放（ADR 0145）。
// 注：assembly attribute 必须置于全局级别；using 统一在 GlobalUsings.cs（System.Runtime.CompilerServices 已声明）。
[assembly: InternalsVisibleTo("NativeGui.Host")]
[assembly: InternalsVisibleTo("JoinCode.Gui.Tests")]
