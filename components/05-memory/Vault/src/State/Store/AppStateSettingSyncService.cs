namespace State;

/// <summary>
/// 配置变更同步服务 — 订阅 IConfigurationService.SettingChanged 事件，将变更同步到 AppState
/// 对齐 TS 版 ConfigTool 中 context.setAppState({ [appStateKey]: finalValue }) 的热更新机制
/// </summary>
[Register]
public sealed partial class AppStateSettingSyncService : IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly IStore<AppState> _store;
    [Inject] private readonly ILogger<AppStateSettingSyncService>? _logger;

    /// <summary>
    /// AppStateKey 到 AppState 字段的映射
    /// 对齐 TS 版 ConfigTool supportedSettings 中的 appStateKey 定义
    /// </summary>
    private static readonly Dictionary<string, Func<AppState, string?, AppState>> s_appStateKeyMappers = new()
    {
        ["Verbose"] = (state, value) => state with { Config = state.Config with { Verbose = value == "true" } },
        ["ThinkingEnabled"] = (state, value) => state with { Config = state.Config with { ThinkingEnabled = value == "true" } },
        ["MainLoopModel"] = (state, value) => state with { Session = state.Session with { CurrentModel = value } },
    };

    public AppStateSettingSyncService(
        IConfigurationService configurationService,
        IStore<AppState> store,
        ILogger<AppStateSettingSyncService>? logger = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger;

        _configurationService.SettingChanged += OnSettingChanged;
    }

    private void OnSettingChanged(object? sender, SettingChangeEventArgs e)
    {
        if (string.IsNullOrEmpty(e.AppStateKey))
            return;

        if (!s_appStateKeyMappers.TryGetValue(e.AppStateKey, out var mapper))
        {
            _logger?.LogDebug("未识别的 AppStateKey: {AppStateKey}，跳过同步", e.AppStateKey);
            return;
        }

        _store.SetState(state =>
        {
            var newState = mapper(state, e.NewValue);
            if (ReferenceEquals(newState, state))
                return state;

            _logger?.LogDebug("配置热更新: {AppStateKey} = {NewValue}", e.AppStateKey, e.NewValue);
            return newState;
        });
    }

    public void Dispose()
    {
        _configurationService.SettingChanged -= OnSettingChanged;
    }
}
