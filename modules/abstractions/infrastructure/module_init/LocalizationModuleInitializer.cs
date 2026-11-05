namespace Infrastructure.Localization;

/// <summary>
/// Infrastructure 程序集模块初始化器 - 确保 L.T() 懒初始化可用
/// 当 Infrastructure.dll 被加载时自动执行，注册 L.LazyInitializer
/// </summary>
internal static class LocalizationModuleInitializer {
#pragma warning disable CA2255 // ModuleInitializer 用于库的自动初始化是合理场景
    /// <summary>
    /// 模块初始化入口 — 注册本地化系统的懒初始化委托，确保 L.T() 首次调用时完成初始化
    /// </summary>
    [ModuleInitializer]
    internal static void Init() {
        L.LazyInitializer = () => LocalizerInitializer.EnsureInitialized();
    }
#pragma warning restore CA2255
}