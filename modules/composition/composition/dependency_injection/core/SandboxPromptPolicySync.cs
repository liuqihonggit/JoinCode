namespace Core.DependencyInjection;

/// <summary>
/// 沙箱提示词策略同步 — 订阅 <c>IConfigurationService.SettingChanged</c>，
/// 让 <c>sandbox.enabled</c> / <c>sandbox.allowUnsandboxedCommands</c> 经 config 工具或 /sandbox 改动后，
/// 下一轮请求的 <see cref="BashToolPrompt"/> 三段式措辞立即生效（不再依赖重启）。
/// </summary>
[Register(typeof(SandboxPromptPolicySync), ServiceLifetime.Singleton)]
public sealed partial class SandboxPromptPolicySync : ServiceEntity, IDisposable {
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<SandboxPromptPolicySync>? _logger;
    private bool _enabled;
    private bool _allowUnsandboxedCommands;
    private bool _disposed;

    /// <summary>
    /// 构造同步服务 — 以启动期 <see cref="WorkflowConfig"/> 快照为初值并订阅配置变更。
    /// </summary>
    /// <param name="configurationService">配置服务。</param>
    /// <param name="config">工作流配置（提供初始 sandbox 快照）。</param>
    /// <param name="logger">日志器（可选）。</param>
    public SandboxPromptPolicySync(
        IConfigurationService configurationService,
        WorkflowConfig config,
        ILogger<SandboxPromptPolicySync>? logger = null) {
        ArgumentNullException.ThrowIfNull(configurationService);
        ArgumentNullException.ThrowIfNull(config);
        _configurationService = configurationService;
        _logger = logger;
        _enabled = config.Sandbox.Enabled;
        _allowUnsandboxedCommands = config.Sandbox.AllowUnsandboxedCommands;
        Apply();

        _configurationService.SettingChanged += OnSettingChanged;
    }

    /// <inheritdoc />
    public override void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;

        _configurationService.SettingChanged -= OnSettingChanged;
        base.Dispose();
    }

    /// <summary>配置变更回调 — 只关心沙箱两个开关。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">变更参数。</param>
    private void OnSettingChanged(object? sender, SettingChangeEventArgs e) {
        switch (e.Key) {
            case ConfigKeys.SandboxEnabled:
                _enabled = IsTrue(e.NewValue);
                break;
            case ConfigKeys.SandboxAllowUnsandboxedCommands:
                _allowUnsandboxedCommands = IsTrue(e.NewValue);
                break;
            default:
                return;
        }

        _logger?.LogDebug("沙箱提示词策略热更新: enabled={Enabled} allowUnsandboxed={Allow}", _enabled, _allowUnsandboxedCommands);
        Apply();
    }

    /// <summary>把当前策略写回提示词快照。</summary>
    private void Apply() => BashToolPrompt.SetSandboxPolicy(_enabled, _allowUnsandboxedCommands);

    /// <summary>解析布尔字符串 — 缺省/空值视为 false，仅 true 为真。</summary>
    /// <param name="value">配置值。</param>
    /// <returns>布尔结果。</returns>
    private static bool IsTrue(string? value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
