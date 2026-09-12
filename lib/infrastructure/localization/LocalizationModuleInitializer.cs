namespace Infrastructure.Localization;

/// <summary>
/// Infrastructure 程序集模块初始化器 - 确保 L.T() 懒初始化可用
/// 当 Infrastructure.dll 被加载时自动执行，注册 L.LazyInitializer
/// </summary>
internal static class LocalizationModuleInitializer
{
#pragma warning disable CA2255 // ModuleInitializer 用于库的自动初始化是合理场景
    [ModuleInitializer]
    internal static void Init()
    {
        L.LazyInitializer = () => LocalizerInitializer.EnsureInitialized();
    }
#pragma warning restore CA2255
}
