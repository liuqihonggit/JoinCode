
namespace Core.Permission;

/// <summary>
/// 权限检查器 - 通过中间件管道管理工具执行权限
/// </summary>
[Register(typeof(IPermissionChecker), ServiceLifetime.Singleton)]
public sealed partial class PermissionChecker : ServiceEntity, IPermissionChecker {
    private readonly MiddlewarePipeline<PermissionCheckContext> _pipeline;
    private readonly ILogger<PermissionChecker>? _logger;
    private readonly HashSet<string> _autoApprovedTools;
    private readonly HashSet<string> _autoRejectedTools;
    private readonly HashSet<CommandDangerLevel> _approvedLevels;
    private readonly PermissionConfig _config;
    private PermissionMode _currentMode;
    private readonly IFileSystem _fs;
    private readonly Task _loadTask;
    private volatile bool _disableBypassGate;

    /// <summary>
    /// Bypass 安全闸 — 从 settings.json 的 disableBypassPermissionsMode 加载
    /// <para>
    /// C2 加固: CLI 显式 flag(--force/--bypass/--permission-mode bypass)也过此闸,
    /// 不仅拦环境变量路径。bridge_serve 的 session_info 上报 bypassAvailable = !此闸。
    /// </para>
    /// </summary>
    public bool DisableBypassGate {
        get => _disableBypassGate;
        set => _disableBypassGate = value;
    }

    /// <summary>
    /// 创建 PermissionChecker
    /// </summary>
    /// <param name="pipeline">权限检查中间件管道</param>
    /// <param name="configOptions">权限配置</param>
    /// <param name="fs">文件系统（用于持久化权限规则）</param>
    /// <param name="logger">日志器</param>
    public PermissionChecker(
        MiddlewarePipeline<PermissionCheckContext> pipeline,
        IOptions<PermissionConfig> configOptions,
        IFileSystem fs,
        ILogger<PermissionChecker>? logger = null) {
        _pipeline = pipeline;
        _logger = logger;
        _config = configOptions.Value;
        _currentMode = PermissionMode.Auto;
        _fs = fs;
        _loadTask = InitializeModeAsync(fs);

        _autoApprovedTools = _config.AutoApprovedTools
            .Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _autoRejectedTools = _config.AutoRejectedTools
            .Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 会话级已批准危险等级 — 用户确认某等级后同会话内同等级自动通过，非持久化
        _approvedLevels = new HashSet<CommandDangerLevel>();
    }

    /// <summary>
    /// 会话级批准某危险等级 — 用户确认后同会话内同等级操作自动通过
    /// 非持久化，每次打开新 exe 重新提示。由 IToolPermissionManager.ApproveLevelTemporarily 调用
    /// </summary>
    public void ApproveLevelTemporarily(CommandDangerLevel level) {
        if (level == CommandDangerLevel.Safe || level == CommandDangerLevel.Dangerous)
            return; // Safe 无需批准，Dangerous 永不批准

        _approvedLevels.Add(level);
        _logger?.LogInformation("危险等级已会话级批准: {Level}", level);
    }

    /// <summary>
    /// 检查某危险等级是否已会话级批准
    /// </summary>
    public bool IsLevelApproved(CommandDangerLevel level) => _approvedLevels.Contains(level);

    /// <summary>
    /// 从 JCC_PERMISSION_MODE 环境变量解析启动时的权限模式 — 支持 E2E 测试自动升级权限
    /// </summary>
    /// <param name="fs">文件系统（用于检查 settings.json 中的 disableBypassPermissionsMode）；为 null 时跳过该检查</param>
    /// <returns>解析到的权限模式；环境变量未设置或无效时返回 null（由调用方回退到 Default）</returns>
    /// <remarks>
    /// 解析规则：
    /// 1. 读取 JCC_PERMISSION_MODE 环境变量
    /// 2. 用 PermissionModeExtensions.FromValue 解析（支持 "bypass"/"plan"/"auto" 等）
    /// 3. 若解析结果为 Bypass 且 settings.json 中 disableBypassPermissionsMode 为真，则返回 null（忽略环境变量，回退 Default）
    /// </remarks>
    internal static async Task<PermissionMode?> TryGetPermissionModeFromEnvAsync(IFileSystem? fs) {
        var envValue = Environment.GetEnvironmentVariable(JccEnvVar.PermissionMode.ToValue());
        if (string.IsNullOrWhiteSpace(envValue))
            return null;

        var parsed = PermissionModeExtensions.FromValue(envValue);
        if (parsed is null)
            return null;

        // 安全闸: settings.json 显式禁用 bypass 模式时，忽略 bypass 环境变量
        if (parsed.Value == PermissionMode.Bypass && await IsDisableBypassPermissionsModeAsync(fs).ConfigureAwait(false)) {
            return null;
        }

        return parsed.Value;
    }

    /// <summary>
    /// 异步初始化权限模式 — IFileSystem 异步化后从构造函数 fire-and-forget 启动；PhysicalFileSystem UTF-8 走 mmap 同步完成
    /// <para>完成时序契约：CheckPermissionAsync 决策前 await _loadTask，确保管道模式为初始化后的最终值</para>
    /// </summary>
    private async Task InitializeModeAsync(IFileSystem? fs) {
        _currentMode = await TryGetPermissionModeFromEnvAsync(fs).ConfigureAwait(false) ?? PermissionMode.Auto;
        _disableBypassGate = await IsDisableBypassPermissionsModeAsync(fs).ConfigureAwait(false);
    }

    /// <summary>
    /// 检查 settings.json 中是否显式禁用 bypass 权限模式
    /// </summary>
    private static async Task<bool> IsDisableBypassPermissionsModeAsync(IFileSystem? fs) {
        if (fs is null)
            return false;

        try {
            var settings = await SettingsLoader.LoadUserSettings(fs).ConfigureAwait(false);
            var flag = settings?.Current?.Permissions?.DisableBypassPermissionsMode;
            if (string.IsNullOrWhiteSpace(flag))
                return false;

            return string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase);
        } catch {
            // settings.json 不存在或解析失败时不阻止 bypass（保持向后兼容）
            return false;
        }
    }

    /// <summary>
    /// 启动模式/安全闸异步加载任务 — bridge 握手（session_info 上报 CurrentMode/DisableBypassGate）前等待，
    /// 确保 fire-and-forget 初始化已完成（PhysicalFileSystem UTF-8 走 mmap 实际同步完成，等待仅兜底）。
    /// </summary>
    public Task ModeInitialization => _loadTask;

    /// <summary>
    /// 当前权限模式
    /// </summary>
    public PermissionMode CurrentMode {
        get => _currentMode;
        set {
            if (value == PermissionMode.Bypass && _disableBypassGate) {
                _logger?.LogWarning("[PermissionChecker] Bypass 模式被 settings.json 安全闸拦截 (disableBypassPermissionsMode=true),保持当前模式: {Mode}", _currentMode);
                return;
            }
            _currentMode = value;
            _logger?.LogInformation("[PermissionChecker] 权限模式切换为: {Mode}", value);
        }
    }

    /// <summary>
    /// 检查工具执行权限 — 通过中间件管道执行
    /// <para>时序契约：决策前等待构造时启动的初始化任务完成（环境变量模式 + settings.json 安全闸），
    /// 保证管道看到的模式是初始化后的最终值。fast path（同步完成的 fs）下 await 已完成 Task 零调度开销。</para>
    /// </summary>
    public async Task<ToolPermissionCheckResult> CheckPermissionAsync(string toolName, Dictionary<string, JsonElement>? arguments = null, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        await _loadTask.ConfigureAwait(false);

        var context = new PermissionCheckContext {
            ToolName = toolName,
            Arguments = arguments ?? [],
            CurrentMode = _currentMode,
            Config = _config,
            AutoApprovedTools = _autoApprovedTools,
            AutoRejectedTools = _autoRejectedTools,
            ApprovedLevels = _approvedLevels
        };

        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        return context.Result ?? ToolPermissionCheckResult.PendingConfirmation($"工具 '{toolName}' 请求执行操作，是否批准？");
    }

    /// <summary>
    /// 添加工具到自动批准列表
    /// </summary>
    public void AddToAutoApproved(string toolName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        _autoApprovedTools.Add(toolName);
        _config.AutoApprovedTools = _config.AutoApprovedTools.SetItem(toolName, new ToolPermissionRule { ToolName = toolName });
    }

    /// <summary>
    /// 添加自动批准规则（含 RuleContent）— 对齐 TS 版 addPermissionRulesToSettings
    /// WebFetch 使用 "domain:example.com" 格式的 RuleContent
    /// </summary>
    public void AddToAutoApproved(string toolName, string? ruleContent) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (string.IsNullOrEmpty(ruleContent)) {
            AddToAutoApproved(toolName);
            return;
        }

        // 域名级规则: 不添加工具名到 HashSet（避免无条件批准所有域名）
        // 只添加带 RuleContent 的规则到配置列表
        _config.AutoApprovedTools = _config.AutoApprovedTools.SetItem(toolName, new ToolPermissionRule {
            ToolName = toolName,
            RuleContent = ruleContent,
            Description = $"Auto-approved: {ruleContent}"
        });
    }

    /// <summary>
    /// 添加自动批准规则并持久化到 settings.json — 对齐 TS 版 persistPermissionUpdate
    /// </summary>
    public async Task AddToAutoApprovedAndPersistAsync(string toolName, string? ruleContent = null, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        AddToAutoApproved(toolName, ruleContent);

        try {
            // 对齐 TS 版: 持久化到 settings.json 的 permissions.allow 数组
            // 格式: "WebFetch(domain:example.com)" — 对齐 TS 版 PermissionRuleValue
            var permissionValue = string.IsNullOrEmpty(ruleContent)
                ? toolName
                : $"{toolName}({ruleContent})";

            // 读取当前 settings.json
            var settings = await SettingsLoader.LoadUserSettingsAsync(_fs, cancellationToken).ConfigureAwait(false) ?? new SettingsJson();

            // 构建新的 allow 列表
            var existingAllow = settings.Current?.Permissions?.Allow ?? [];
            if (existingAllow.Contains(permissionValue, StringComparer.OrdinalIgnoreCase))
                return;

            var updatedAllow = new List<string>(existingAllow) { permissionValue };

            var updatedPermissions = new PermissionsSettings {
                Allow = updatedAllow,
                Deny = settings.Current?.Permissions?.Deny ?? [],
                Ask = settings.Current?.Permissions?.Ask ?? [],
                DefaultMode = settings.Current?.Permissions?.DefaultMode,
                AdditionalDirectories = settings.Current?.Permissions?.AdditionalDirectories ?? [],
                DisableBypassPermissionsMode = settings.Current?.Permissions?.DisableBypassPermissionsMode,
            };

            var updatedSettings = new SettingsJson {
                Vendor = settings.Vendor,
                Current = new CurrentSettings(settings.Current ?? new CurrentSettings()) { Permissions = updatedPermissions },
            };
            await SettingsLoader.SaveSettingsAsync(_fs, SettingSource.UserSettings, updatedSettings, cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("已持久化权限规则: {PermissionValue}", permissionValue);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "持久化权限规则失败: {ToolName}({RuleContent})", toolName, ruleContent);
        }
    }

    /// <summary>
    /// 添加工具到自动拒绝列表
    /// </summary>
    public void AddToAutoRejected(string toolName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        _autoRejectedTools.Add(toolName);
        _config.AutoRejectedTools = _config.AutoRejectedTools.SetItem(toolName, new ToolPermissionRule { ToolName = toolName });
    }

    /// <summary>
    /// 从自动批准列表移除工具
    /// </summary>
    public void RemoveFromAutoApproved(string toolName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        _autoApprovedTools.Remove(toolName);
        _config.AutoApprovedTools = _config.AutoApprovedTools.Remove(toolName);
    }

    /// <summary>
    /// 从自动拒绝列表移除工具
    /// </summary>
    public void RemoveFromAutoRejected(string toolName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        _autoRejectedTools.Remove(toolName);
        _config.AutoRejectedTools = _config.AutoRejectedTools.Remove(toolName);
    }
}