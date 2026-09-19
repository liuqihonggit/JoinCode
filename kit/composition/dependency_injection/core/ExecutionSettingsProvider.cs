namespace Core.DependencyInjection;

/// <summary>
/// 执行设置提供者 — 对齐 CLI JoinCode.Services.ExecutionSettingsProvider（已下沉共享层）。
/// 从 settings.json 懒加载持久化的 effortLevel，供 ChatOptionsFactory / EffortLevelMiddleware 消费。
/// </summary>
[Register(typeof(IExecutionSettingsProvider), ServiceLifetime.Singleton)]
public sealed partial class ExecutionSettingsProvider : ServiceEntity, IExecutionSettingsProvider {
    private readonly WorkflowConfig _config;
    private readonly ITelemetryService? _telemetryService;
    private readonly IFileSystem _fs;
    private readonly IProviderDefinitionRegistry _registry;

    /// <summary>
    /// 初始化 <see cref="ExecutionSettingsProvider"/> 实例。
    /// </summary>
    /// <param name="config">工作流配置（提供 FastMode、Provider 等静态设置）。</param>
    /// <param name="fs">文件系统（用于读取 settings.json 持久化设置）。</param>
    /// <param name="registry">供应商定义注册表（用于解析 FastModelId）。</param>
    /// <param name="telemetryService">可选的遥测服务（记录设置变更计数）。</param>
    public ExecutionSettingsProvider(WorkflowConfig config, IFileSystem fs, IProviderDefinitionRegistry registry, ITelemetryService? telemetryService = null) {
        _config = config;
        _telemetryService = telemetryService;
        _fs = fs;
        _registry = registry;
    }

    private EffortLevel LoadPersistedEffort() {
        // 从 settings.json 读取持久化的 effortLevel — 对齐 TS getUserSpecifiedModelSetting
        var persistedEffort = ConfigLoader.LoadSettingFromSettingsJson("effortLevel", _fs);
        return EffortLevelHelper.ParseEffortLevel(persistedEffort) ?? EffortLevel.Auto;
    }

    // 双变量模式（规则3）：首次读取触发持久化加载；set 立即生效并标记已加载。
    // 修复原实现 bug：Lazy 未求值时 getter 返回字段默认 Low，且 set 后 Lazy 已求值导致 getter 返回旧值。
    private EffortLevel _effortLevel = EffortLevel.Auto;
    private bool _isLoaded;

    /// <summary>
    /// 获取或设置努力级别。首次读取触发从 settings.json 惰加载（双变量模式，规则3）；
    /// 设置时立即生效并标记已加载，同时记录遥测计数。
    /// </summary>
    public EffortLevel EffortLevel {
        get {
            if (!_isLoaded) {
                _effortLevel = LoadPersistedEffort();
                _isLoaded = true;
            }
            return _effortLevel;
        }
        set {
            if (_effortLevel != value) {
                _telemetryService?.RecordCount("host.settings.change.count", new Dictionary<string, string> { ["setting"] = "effortLevel", ["old"] = _effortLevel.ToValue(), ["new"] = value.ToValue() }, "count", "Execution settings change count");
            }
            _effortLevel = value;
            _isLoaded = true;
        }
    }
    /// <summary>
    /// 获取是否启用快速模式（从 <see cref="WorkflowConfig.FastMode"/> 读取）。
    /// </summary>
    public bool FastMode => _config.FastMode;
    /// <summary>
    /// 获取快速模式使用的模型标识（从供应商定义注册表解析 <see cref="WorkflowConfig.Provider"/> 的 DefaultFastModelId）。
    /// </summary>
    public string? FastModelId => _registry.TryGet(_config.Provider?.Vendor ?? string.Empty)?.DefaultFastModelId;

    // 温度/最大长度 — CLI 不设置（null）→ ChatOptionsFactory 回退 LlmParameters.Chat，行为不变。
    // GUI 滑块变更时经会话写回此属性（双变量模式无需 staging：值是瞬时覆盖，不持久化）。
    /// <summary>
    /// 获取或设置温度参数。CLI 不设置（null）时 <c>ChatOptionsFactory</c> 回退到 <c>LlmParameters.Chat</c>；
    /// GUI 滑块变更时经会话写回此属性（双变量模式无需 staging：值是瞬时覆盖，不持久化）。
    /// </summary>
    public float? Temperature { get; set; }
    /// <summary>
    /// 获取或设置最大输出 token 数。CLI 不设置（null）时 <c>ChatOptionsFactory</c> 回退到 <c>LlmParameters.Chat</c>；
    /// GUI 变更时经会话写回此属性（瞬时覆盖，不持久化）。
    /// </summary>
    public int? MaxTokens { get; set; }

    // 思考模式开关 — 从 settings.json 的 alwaysThinkingEnabled 懒加载（双变量模式，对齐 EffortLevel）
    private bool _thinkingEnabled;
    private bool _isThinkingLoaded;

    /// <summary>
    /// 获取或设置是否启用思考模式。首次读取从 settings.json 的 <c>alwaysThinkingEnabled</c> 惰加载（双变量模式，对齐 <see cref="EffortLevel"/>）；
    /// 设置时立即生效并标记已加载。
    /// </summary>
    public bool ThinkingEnabled {
        get {
            if (!_isThinkingLoaded) {
                _thinkingEnabled = LoadPersistedThinkingEnabled();
                _isThinkingLoaded = true;
            }
            return _thinkingEnabled;
        }
        set {
            _thinkingEnabled = value;
            _isThinkingLoaded = true;
        }
    }

    private bool LoadPersistedThinkingEnabled() {
        var persisted = ConfigLoader.LoadSettingFromSettingsJson("alwaysThinkingEnabled", _fs);
        return string.Equals(persisted, "true", StringComparison.OrdinalIgnoreCase);
    }

}