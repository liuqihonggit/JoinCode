namespace Guard.Tests.Configuration;

/// <summary>
/// 共享 AppDataConstants 全局静态状态的测试串行执行集合。
/// 包含: SettingsLoaderTests (修改 AppDataFolder) + ProjectRulesLoaderTests (读取 AppDataFolder 静态字段)
/// </summary>
[CollectionDefinition("AppDataConstantsCollection", DisableParallelization = true)]
public sealed class AppDataConstantsCollection
{
}
