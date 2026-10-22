namespace Composition.Tests.Commands;

/// <summary>
/// 斜杠命令输出共享集合 — Console.SetOut 为全局重定向,凡捕获输出的命令测试类
/// 必须归入同一 collection 串行执行,避免并行测试间 StringWriter 互串导致 flaky
/// </summary>
[CollectionDefinition("command-output")]
public sealed class CommandOutputCollection;
