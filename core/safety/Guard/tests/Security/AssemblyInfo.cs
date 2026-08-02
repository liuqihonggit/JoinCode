namespace Guard.Tests;

/// <summary>
/// 确保本地化系统在测试程序集加载时自动初始化为中文
/// </summary>
internal static class AssemblyLocalizationInit
{
#pragma warning disable CA2255 // ModuleInitializer 用于测试程序集的自动初始化是合理场景
    [ModuleInitializer]
    internal static void Init() => LocalizerInitializer.EnsureInitialized();
#pragma warning restore CA2255
}
