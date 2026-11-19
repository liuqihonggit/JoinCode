namespace Infrastructure.Localization;

/// <summary>
/// Infrastructure 程序集模块初始化器 — 确保 L.T() 懒初始化可用。
/// <para>必须留在 Infrastructure 程序集：所有消费方（CLI/TUI/GUI/测试宿主）都传递引用该程序集，
/// 程序集加载即注册 L.LazyInitializer；放到上层可执行程序集只有 CLI 宿主生效，其余宿主本地化退化为返回 key。</para>
/// </summary>
internal static class LocalizationModuleInitializer {
    /// <summary>
    /// 模块初始化入口 — 注册本地化系统的懒初始化委托，确保 L.T() 首次调用时完成初始化
    /// </summary>
    [ModuleInitializer]
    internal static void Init() {
        L.LazyInitializer = () => LocalizerInitializer.EnsureInitialized();
    }
}
