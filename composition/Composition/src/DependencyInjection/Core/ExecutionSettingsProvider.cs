using JoinCode.Abstractions.Configuration.Providers;

namespace Core.DependencyInjection;

/// <summary>
/// 执行设置提供者 — 对齐 CLI JoinCode.Services.ExecutionSettingsProvider（已下沉共享层）。
/// 从 settings.json 懒加载持久化的 effortLevel，供 ChatOptionsFactory / EffortLevelMiddleware 消费。
/// </summary>
[Register(typeof(IExecutionSettingsProvider))]
public sealed partial class ExecutionSettingsProvider : ServiceEntity, IExecutionSettingsProvider
{
    private readonly WorkflowConfig _config;
    private readonly ITelemetryService? _telemetryService;
    private readonly IFileSystem _fs;
    private readonly IProviderDefinitionRegistry _registry;

    public ExecutionSettingsProvider(WorkflowConfig config, IFileSystem fs, IProviderDefinitionRegistry registry, ITelemetryService? telemetryService = null)
    {
        _config = config;
        _telemetryService = telemetryService;
        _fs = fs;
        _registry = registry;
    }

    private EffortLevel LoadPersistedEffort()
    {
        // 从 settings.json 读取持久化的 effortLevel — 对齐 TS getUserSpecifiedModelSetting
        var persistedEffort = ConfigLoader.LoadSettingFromSettingsJson("effortLevel", _fs);
        return EffortLevelHelper.ParseEffortLevel(persistedEffort) ?? EffortLevel.Auto;
    }

    // 双变量模式（规则3）：首次读取触发持久化加载；set 立即生效并标记已加载。
    // 修复原实现 bug：Lazy 未求值时 getter 返回字段默认 Low，且 set 后 Lazy 已求值导致 getter 返回旧值。
    private EffortLevel _effortLevel = EffortLevel.Auto;
    private bool _isLoaded;

    public EffortLevel EffortLevel
    {
        get
        {
            if (!_isLoaded)
            {
                _effortLevel = LoadPersistedEffort();
                _isLoaded = true;
            }
            return _effortLevel;
        }
        set
        {
            if (_effortLevel != value)
            {
                _telemetryService?.RecordCount("host.settings.change.count", new Dictionary<string, string> { ["setting"] = "effortLevel", ["old"] = _effortLevel.ToValue(), ["new"] = value.ToValue() }, "count", "Execution settings change count");
            }
            _effortLevel = value;
            _isLoaded = true;
        }
    }
    public bool FastMode => _config.FastMode;
    public string? FastModelId => _registry.TryGet(_config.Provider?.Provider ?? string.Empty)?.DefaultFastModelId;

}
