namespace JoinCode;

/// <summary>
/// CLI 应用模块初始化器 — 在 Main 之前注册本地化系统的懒初始化委托。
/// 放在可执行程序集内：CA2255 要求 ModuleInitializer 仅用于应用代码；库侧由显式 Initialize 调用初始化。
/// </summary>
internal static class LocalizationModuleInitializer {
    /// <summary>
    /// 模块初始化入口 — 注册本地化系统的懒初始化委托，确保 L.T() 首次调用时完成初始化
    /// </summary>
    [ModuleInitializer]
    internal static void Init() {
        L.LazyInitializer = () => Infrastructure.Localization.LocalizerInitializer.EnsureInitialized();
    }
}
