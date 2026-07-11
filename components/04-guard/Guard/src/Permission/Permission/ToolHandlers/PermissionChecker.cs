
namespace Core.Permission;

/// <summary>
/// 权限检查器 - 通过中间件管道管理工具执行权限
/// </summary>
[Register]
public sealed partial class PermissionChecker : IPermissionChecker
{
    private readonly MiddlewarePipeline<PermissionCheckContext> _pipeline;
    [Inject] private readonly ILogger<PermissionChecker>? _logger;
    private readonly HashSet<string> _autoApprovedTools;
    private readonly HashSet<string> _autoRejectedTools;
    private readonly PermissionConfig _config;
    private PermissionMode _currentMode;
    private readonly IFileSystem _fs;

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
        ILogger<PermissionChecker>? logger = null)
    {
        _pipeline = pipeline;
        _logger = logger;
        _config = configOptions.Value;
        _currentMode = TryGetPermissionModeFromEnv(fs) ?? PermissionMode.Default;
        _fs = fs;

        // 确保列表不为 null
        _config.AutoApprovedTools ??= [];
        _config.AutoRejectedTools ??= [];

        _autoApprovedTools = _config.AutoApprovedTools
            .Select(r => r.ToolName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _autoRejectedTools = _config.AutoRejectedTools
            .Select(r => r.ToolName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从 JCC_PERMISSION_MODE 环境变量解析启动时的权限模式 — 支持 E2E 测试自动升级权限
    /// </summary>
    /// <param name="fs">文件系统（用于检查 settings.json 中的 disableBypassPermissionsMode）；为 null 时跳过该检查</param>
    /// <returns>解析到的权限模式；环境变量未设置或无效时返回 null（由调用方回退到 Default）</returns>
    /// <remarks>
    /// 解析规则：
    /// 1. 读取 JCC_PERMISSION_MODE 环境变量
    /// 2. 用 PermissionModeExtensions.FromValue 解析（支持 "bypassPermissions"/"plan"/"auto" 等）
    /// 3. 若解析结果为 BypassPermissions 且 settings.json 中 disableBypassPermissionsMode 为真，则返回 null（忽略环境变量，回退 Default）
    /// </remarks>
    internal static PermissionMode? TryGetPermissionModeFromEnv(IFileSystem? fs)
    {
        var envValue = Environment.GetEnvironmentVariable(JccEnvVar.PermissionMode.ToValue());
        if (string.IsNullOrWhiteSpace(envValue))
            return null;

        var parsed = PermissionModeExtensions.FromValue(envValue);
        if (parsed is null)
            return null;

        // 安全闸: settings.json 显式禁用 bypass 模式时，忽略 bypassPermissions 环境变量
        if (parsed.Value == PermissionMode.BypassPermissions && IsDisableBypassPermissionsMode(fs))
        {
            return null;
        }

        return parsed.Value;
    }

    /// <summary>
    /// 检查 settings.json 中是否显式禁用 bypass 权限模式
    /// </summary>
    private static bool IsDisableBypassPermissionsMode(IFileSystem? fs)
    {
        if (fs is null)
            return false;

        try
        {
            var settings = SettingsLoader.LoadUserSettings(fs);
            var flag = settings?.Permissions?.DisableBypassPermissionsMode;
            if (string.IsNullOrWhiteSpace(flag))
                return false;

            return string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // settings.json 不存在或解析失败时不阻止 bypass（保持向后兼容）
            return false;
        }
    }

    /// <summary>
    /// 当前权限模式
    /// </summary>
    public PermissionMode CurrentMode
    {
        get => _currentMode;
        set
        {
            _currentMode = value;
            _logger?.LogInformation("[PermissionChecker] 权限模式切换为: {Mode}", value);
        }
    }

    /// <summary>
    /// 检查工具执行权限 — 通过中间件管道执行
    /// </summary>
    public async Task<ToolPermissionCheckResult> CheckPermissionAsync(string toolName, Dictionary<string, JsonElement>? arguments = null, CancellationToken cancellationToken = default)
    {
        var context = new PermissionCheckContext
        {
            ToolName = toolName,
            Arguments = arguments,
            CurrentMode = _currentMode,
            Config = _config,
            AutoApprovedTools = _autoApprovedTools,
            AutoRejectedTools = _autoRejectedTools
        };

        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        return context.Result ?? ToolPermissionCheckResult.PendingConfirmation($"工具 '{toolName}' 请求执行操作，是否批准？");
    }

    /// <summary>
    /// 添加工具到自动批准列表
    /// </summary>
    public void AddToAutoApproved(string toolName)
    {
        _autoApprovedTools.Add(toolName);
        _config.AutoApprovedTools.Add(new ToolPermissionRule { ToolName = toolName });
    }

    /// <summary>
    /// 添加自动批准规则（含 RuleContent）— 对齐 TS 版 addPermissionRulesToSettings
    /// WebFetch 使用 "domain:example.com" 格式的 RuleContent
    /// </summary>
    public void AddToAutoApproved(string toolName, string? ruleContent)
    {
        if (string.IsNullOrEmpty(ruleContent))
        {
            AddToAutoApproved(toolName);
            return;
        }

        // 域名级规则: 不添加工具名到 HashSet（避免无条件批准所有域名）
        // 只添加带 RuleContent 的规则到配置列表
        _config.AutoApprovedTools.Add(new ToolPermissionRule
        {
            ToolName = toolName,
            RuleContent = ruleContent,
            Description = $"Auto-approved: {ruleContent}"
        });
    }

    /// <summary>
    /// 添加自动批准规则并持久化到 settings.json — 对齐 TS 版 persistPermissionUpdate
    /// </summary>
    public async Task AddToAutoApprovedAndPersistAsync(string toolName, string? ruleContent = null, CancellationToken cancellationToken = default)
    {
        AddToAutoApproved(toolName, ruleContent);

        try
        {
            // 对齐 TS 版: 持久化到 settings.json 的 permissions.allow 数组
            // 格式: "WebFetch(domain:example.com)" — 对齐 TS 版 PermissionRuleValue
            var permissionValue = string.IsNullOrEmpty(ruleContent)
                ? toolName
                : $"{toolName}({ruleContent})";

            // 读取当前 settings.json
            var settings = await SettingsLoader.LoadUserSettingsAsync(_fs, cancellationToken).ConfigureAwait(false) ?? new SettingsJson();

            // 构建新的 allow 列表
            var existingAllow = settings.Permissions?.Allow ?? [];
            if (existingAllow.Contains(permissionValue, StringComparer.OrdinalIgnoreCase))
                return; // 已存在，无需重复

            var updatedAllow = new List<string>(existingAllow) { permissionValue };

            // 创建新的 PermissionsSettings（sealed class 不支持 with 表达式，手动构建）
            var updatedPermissions = new PermissionsSettings
            {
                Allow = updatedAllow,
                Deny = settings.Permissions?.Deny,
                Ask = settings.Permissions?.Ask,
                DefaultMode = settings.Permissions?.DefaultMode,
                AdditionalDirectories = settings.Permissions?.AdditionalDirectories,
                DisableBypassPermissionsMode = settings.Permissions?.DisableBypassPermissionsMode,
            };

            // 创建新的 SettingsJson 并保存
            var updatedSettings = new SettingsJson(settings) { Permissions = updatedPermissions };
            await SettingsLoader.SaveSettingsAsync(_fs, SettingSource.UserSettings, updatedSettings, cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("已持久化权限规则: {PermissionValue}", permissionValue);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "持久化权限规则失败: {ToolName}({RuleContent})", toolName, ruleContent);
        }
    }

    /// <summary>
    /// 添加工具到自动拒绝列表
    /// </summary>
    public void AddToAutoRejected(string toolName)
    {
        _autoRejectedTools.Add(toolName);
        _config.AutoRejectedTools.Add(new ToolPermissionRule { ToolName = toolName });
    }

    /// <summary>
    /// 从自动批准列表移除工具
    /// </summary>
    public void RemoveFromAutoApproved(string toolName)
    {
        _autoApprovedTools.Remove(toolName);
        _config.AutoApprovedTools.RemoveAll(r =>
            r.ToolName.Equals(toolName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 从自动拒绝列表移除工具
    /// </summary>
    public void RemoveFromAutoRejected(string toolName)
    {
        _autoRejectedTools.Remove(toolName);
        _config.AutoRejectedTools.RemoveAll(r =>
            r.ToolName.Equals(toolName, StringComparison.OrdinalIgnoreCase));
    }
}
