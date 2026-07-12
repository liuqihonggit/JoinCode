namespace JoinCode.Services;

[Register]
public sealed class ExecutionSettingsProvider : IExecutionSettingsProvider
{
    private readonly WorkflowConfig _config;
    private readonly ITelemetryService? _telemetryService;
    private readonly IFileSystem _fs;
    private readonly Lazy<EffortLevel> _effortLevelLazy;

    public ExecutionSettingsProvider(WorkflowConfig config, IFileSystem fs, ITelemetryService? telemetryService = null)
    {
        _config = config;
        _telemetryService = telemetryService;
        _fs = fs;
        // 延迟加载 effortLevel — 避免构造函数中阻塞调用 async 方法
        _effortLevelLazy = new Lazy<EffortLevel>(LoadPersistedEffort, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private EffortLevel LoadPersistedEffort()
    {
        // 从 settings.json 读取持久化的 effortLevel — 对齐 TS getUserSpecifiedModelSetting
        var persistedEffort = ConfigLoader.LoadSettingFromSettingsJsonAsync("effortLevel", _fs)
            .GetAwaiter().GetResult();
        return EffortLevelHelper.ParseEffortLevel(persistedEffort) ?? EffortLevel.Auto;
    }

    private EffortLevel _effortLevel;
    public EffortLevel EffortLevel
    {
        get => _effortLevelLazy.IsValueCreated ? _effortLevelLazy.Value : _effortLevel;
        set
        {
            if (_effortLevel != value)
            {
                _telemetryService?.RecordCount("host.settings.change.count", new Dictionary<string, string> { ["setting"] = "effortLevel", ["old"] = _effortLevel.ToValue(), ["new"] = value.ToValue() }, "count", "Execution settings change count");
            }
            _effortLevel = value;
        }
    }
    public bool FastMode => _config.FastMode;
    public string? FastModelId => ProviderDefinitionRegistry.TryGetStatic(_config.Provider?.Provider ?? string.Empty)?.DefaultFastModelId;

}
