namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 属性变更副作用门控 — 封装三个标志位（偏好加载/热重载/外部主题），
/// 统一判断是否允许持久化副作用，用 using scope 替代手动 true/false 对。
/// </summary>
public sealed class PropertyChangeGate {
    private volatile bool _preferencesLoaded;
    private volatile bool _refreshingConfig;
    private volatile bool _applyingTheme;

    /// <summary>副作用门是否开放（偏好已加载 &amp;&amp; 非热重载 &amp;&amp; 非外部主题应用）</summary>
    public bool IsOpen => _preferencesLoaded && !_refreshingConfig && !_applyingTheme;

    /// <summary>偏好是否已加载（供诊断日志与 SaveHotkeysToPreferences 守卫）</summary>
    public bool PreferencesLoaded => _preferencesLoaded;

    /// <summary>是否正在热重载配置（供诊断日志）</summary>
    public bool RefreshingConfig => _refreshingConfig;

    /// <summary>是否正在应用外部主题（供诊断日志）</summary>
    public bool ApplyingTheme => _applyingTheme;

    /// <summary>标记偏好加载完成（一次性，加载后不再变）</summary>
    public void MarkPreferencesLoaded() => _preferencesLoaded = true;

    /// <summary>标记偏好正在加载（加载期间不回写）</summary>
    public void MarkPreferencesLoading() => _preferencesLoaded = false;

    /// <summary>进入热重载配置作用域（using var 自动退出，防止忘记 reset）</summary>
    public IDisposable EnterRefreshingConfigScope()
        => new Scope(() => _refreshingConfig = true, () => _refreshingConfig = false);

    /// <summary>进入外部主题应用作用域（using var 自动退出）</summary>
    public IDisposable EnterApplyingThemeScope()
        => new Scope(() => _applyingTheme = true, () => _applyingTheme = false);

    private sealed class Scope : IDisposable {
        private readonly Action _exit;

        /// <summary>构造作用域 — 立即执行 enter，Dispose 时执行 exit</summary>
        public Scope(Action enter, Action exit) {
            _exit = exit;
            enter();
        }

        /// <summary>退出作用域 — 执行 exit 回调恢复标志位</summary>
        public void Dispose() => _exit();
    }
}