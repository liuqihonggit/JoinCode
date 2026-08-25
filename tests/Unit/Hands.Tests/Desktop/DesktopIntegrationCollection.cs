namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// Desktop 集成测试串行化集合 — 防止多个集成测试并行运行时启动多个记事本互相干扰
/// </summary>
[CollectionDefinition("DesktopIntegration")]
public sealed class DesktopIntegrationCollection;
