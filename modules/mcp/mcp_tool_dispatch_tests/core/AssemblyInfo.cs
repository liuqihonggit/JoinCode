namespace McpToolDispatch.Tests;

/// <summary>
/// 确保本地化系统在测试程序集加载时自动初始化为中文
/// </summary>
internal static class AssemblyLocalizationInit {
    [ModuleInitializer]
    internal static void Init() => Infrastructure.Localization.LocalizerInitializer.EnsureInitialized();
}