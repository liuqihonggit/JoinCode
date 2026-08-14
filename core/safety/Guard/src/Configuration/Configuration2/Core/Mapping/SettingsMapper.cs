
namespace Core.Configuration;

/// <summary>
/// 将 SettingsJson + 环境变量覆盖映射到 WorkflowConfig
/// 优先级: 环境变量 > SettingsJson 字段 > Provider 定义默认值 > 内置默认值
/// </summary>
[Register]
public sealed partial class SettingsMapper : ServiceEntity
{
    private readonly IProviderDefinitionRegistry _registry;

    public SettingsMapper(IProviderDefinitionRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// 将 SettingsJson 映射到 WorkflowConfig，并应用环境变量覆盖
    /// </summary>
    public WorkflowConfig ToWorkflowConfig(SettingsJson? settings)
    {
        var config = new WorkflowConfig();

        // Provider 配置 — 从 vendor[current.profile] + current 偏好映射
        ApplyProviderSettings(config, settings);

        // 代码执行配置
        ApplyCodeExecutionSettings(config, settings);

        // Worktree 配置
        ApplyWorktreeSettings(config, settings);

        // 快速模式
        config.FastMode = settings?.Current?.FastMode ?? false;

        // 工具评分配置
        ApplyToolScoreSettings(config, settings);

        return config;
    }

    /// <summary>
    /// 应用环境变量覆盖到已映射的 WorkflowConfig
    /// 环境变量优先级最高，覆盖所有文件配置
    /// 注意: API Key 不在此处理，由 ConfigLoader.ResolveApiKeyAsync 统一解析
    /// </summary>
    public void ApplyEnvOverrides(WorkflowConfig config, SettingsJson? settings = null)
    {
        // Provider 环境变量覆盖
        var envProvider = Environment.GetEnvironmentVariable(JccEnvVar.Vendor.ToValue());
        if (!string.IsNullOrEmpty(envProvider) && config.Provider.Vendor != envProvider)
        {
            config.Provider.Vendor = envProvider;

            // --vendor 自动匹配 vendor 字典中的同名预设
            ApplyProfileFromVendor(envProvider, config, settings);

            // Provider 变更时，重新应用 Provider 定义的默认值
            var newDefinition = _registry.TryGet(envProvider)
                ?? throw new ConfigurationException(
                    $"未知的 Provider '{envProvider}'，可用值: {string.Join(", ", _registry.RegisteredProviders)}。");

            config.Provider.Endpoint ??= newDefinition.DefaultEndpoint;
            config.Provider.Definition = newDefinition;
            config.Provider.Protocol = newDefinition.Protocol.ToValue();

            // 仅当 ModelId 未被显式设置时，使用新 Provider 的默认模型
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(JccEnvVar.ModelId.ToValue())))
            {
                config.Provider.ModelId ??= newDefinition.DefaultModelId
                    ?? throw new ConfigurationException(
                        $"Provider '{newDefinition.ProviderName}' 没有定义默认模型，请通过 {JccEnvVar.ModelId.ToValue()} 环境变量指定模型。");
            }
        }

        // JCC_PROTOCOL 环境变量覆盖
        var envProtocol = Environment.GetEnvironmentVariable(JccEnvVar.Protocol.ToValue());
        if (!string.IsNullOrEmpty(envProtocol))
            config.Provider.Protocol = envProtocol;

        // JCC_MODEL_ID / JCC_ENDPOINT / JCC_PROFILE 已由 EnvOverrideApplier 在 SettingsJson 层覆盖，
        // ToWorkflowConfig 映射时已生效，此处不再重复处理

        var envOrgId = Environment.GetEnvironmentVariable(JccEnvVar.OrganizationId.ToValue());
        if (!string.IsNullOrEmpty(envOrgId))
            config.Provider.OrganizationId = envOrgId;

        var envApiVersion = Environment.GetEnvironmentVariable(JccEnvVar.ApiVersion.ToValue());
        if (!string.IsNullOrEmpty(envApiVersion))
            config.Provider.ApiVersion = envApiVersion;

        var envOAuth = Environment.GetEnvironmentVariable(JccEnvVar.EnableOAuth.ToValue());
        if (bool.TryParse(envOAuth, out var enableOAuth))
            config.Provider.EnableOAuthTokenSupport = enableOAuth;

        // 代码执行环境变量覆盖
        var envTimeout = Environment.GetEnvironmentVariable(JccEnvVar.CodeExecutionTimeout.ToValue());
        if (int.TryParse(envTimeout, out var timeout))
            config.CodeExecution.ExecutionTimeoutSeconds = timeout;

        var envMaxMemory = Environment.GetEnvironmentVariable(JccEnvVar.CodeExecutionMaxMemory.ToValue());
        if (int.TryParse(envMaxMemory, out var maxMemory))
            config.CodeExecution.MaxMemoryMB = maxMemory;

        // Provider 定义的端点环境变量覆盖
        ApplyProviderDefinitionEndpointEnvOverrides(config);

        var envStateFilePath = Environment.GetEnvironmentVariable(JccEnvVar.StateFilePath.ToValue());
        if (!string.IsNullOrEmpty(envStateFilePath))
            config.StateFilePath = envStateFilePath;
    }

    /// <summary>
    /// 从 SettingsJson 的 env 字段注入环境变量到当前进程
    /// </summary>
    public static void InjectEnvFromSettings(SettingsJson? settings)
    {
        if (settings?.Current?.Env is null) return;

        foreach (var (key, value) in settings.Current.Env)
        {
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    /// <summary>
    /// 合并两个 SettingsJson（低优先级 + 高优先级）— 委托给 SettingsJson.Merge
    /// </summary>
    public static SettingsJson Merge(SettingsJson? baseSettings, SettingsJson? overrideSettings)
        => SettingsJson.Merge(baseSettings, overrideSettings);

    #region 内部方法

    private void ApplyProviderSettings(WorkflowConfig config, SettingsJson? settings)
    {
        var current = settings?.Current;
        var profile = settings?.GetActiveProfile();

        // Provider — 优先从 profile 读取，回退到 profile 名本身
        if (profile is not null)
        {
            if (!string.IsNullOrEmpty(profile.Provider))
                config.Provider.Vendor = profile.Provider;
            else if (!string.IsNullOrEmpty(current?.Profile))
                config.Provider.Vendor = current.Profile;
        }
        else if (!string.IsNullOrEmpty(current?.Profile))
        {
            config.Provider.Vendor = current.Profile;
        }

        // Endpoint — 从 profile 读取
        if (!string.IsNullOrEmpty(profile?.Endpoint))
        {
            config.Provider.Endpoint = profile.Endpoint;
        }

        // Provider 定义自动配置默认值
        var definition = _registry.TryGet(config.Provider.Vendor);
        if (definition is not null)
        {
            config.Provider.Endpoint ??= definition.DefaultEndpoint;
            config.Provider.Definition = definition;
            config.Provider.Protocol = definition.Protocol.ToValue();
        }

        // Model ID — 优先从 profile 读取，回退到 Provider 定义默认模型
        if (!string.IsNullOrEmpty(profile?.Model))
        {
            config.Provider.ModelId = profile.Model;
        }
        else if (definition is not null)
        {
            config.Provider.ModelId = definition.DefaultModelId
                ?? throw new ConfigurationException(
                    $"Provider '{definition.ProviderName}' 没有定义默认模型，请通过 vendor[current.profile].model 或 {JccEnvVar.ModelId.ToValue()} 环境变量指定模型。");
        }
        else
        {
            throw new ConfigurationException(
                $"未知的 Provider '{config.Provider.Vendor}'，可用值: {string.Join(", ", _registry.RegisteredProviders)}。" +
                $"请通过 {JccEnvVar.Vendor.ToValue()} 环境变量指定正确的 Provider。");
        }

        // CurrentProfile — 从 current.profile 读取
        if (!string.IsNullOrEmpty(current?.Profile))
            config.CurrentProfile = current.Profile;

        // API Version
        config.Provider.ApiVersion ??= definition?.DefaultApiVersion ?? "2024-02-01";
    }

    private static void ApplyCodeExecutionSettings(WorkflowConfig config, SettingsJson? settings)
    {
        var sandbox = settings?.Current?.Sandbox;
        if (sandbox is null) return;

        if (sandbox.Enabled.HasValue)
            config.CodeExecution.ReadOnlyFilesystem = sandbox.Enabled.Value;

        if (sandbox.RestrictNetwork.HasValue)
            config.CodeExecution.AllowNetworkAccess = !sandbox.RestrictNetwork.Value;

        if (sandbox.MemoryLimitMb.HasValue && sandbox.MemoryLimitMb.Value > 0)
            config.CodeExecution.MaxMemoryMB = sandbox.MemoryLimitMb.Value;

        if (sandbox.AllowedPaths is not null && sandbox.AllowedPaths.Count > 0)
            config.CodeExecution.AllowedDirectories = string.Join(";", sandbox.AllowedPaths);
    }

    private static void ApplyWorktreeSettings(WorkflowConfig config, SettingsJson? settings)
    {
        var worktree = settings?.Current?.Worktree;
        if (worktree is null) return;

        if (worktree.SparsePaths is not null)
            config.Worktree.SparsePaths = worktree.SparsePaths;

        if (worktree.SymlinkDirectories is not null)
            config.Worktree.SymlinkDirectories = worktree.SymlinkDirectories;
    }

    private static void ApplyProviderDefinitionEndpointEnvOverrides(WorkflowConfig config)
    {
        if (config.Provider.Definition is not { } definition) return;

        var envEndpoint = definition.ResolveEndpointFromEnv();
        if (!string.IsNullOrEmpty(envEndpoint))
            config.Provider.Endpoint = envEndpoint;
    }

    /// <summary>
    /// --vendor 自动匹配 vendor 字典中的同名预设
    /// </summary>
    private static void ApplyProfileFromVendor(string vendor, WorkflowConfig config, SettingsJson? settings)
    {
        if (settings is null)
        {
            var fs = new IO.FileSystem.PhysicalFileSystem();
            settings = ConfigLoader.LoadSettingsJsonAsync(fs).GetAwaiter().GetResult();
        }

        if (settings?.Vendor is null || !settings.Vendor.TryGetValue(vendor, out var profile))
            return;

        if (!string.IsNullOrEmpty(profile.Model) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(JccEnvVar.ModelId.ToValue())))
            config.Provider.ModelId = profile.Model;

        if (!string.IsNullOrEmpty(profile.Endpoint))
            config.Provider.Endpoint = profile.Endpoint;

        config.CurrentProfile = vendor;
    }

    private static void ApplyToolScoreSettings(WorkflowConfig config, SettingsJson? settings)
    {
        var current = settings?.Current;
        if (current is null) return;

        if (current.ToolScore is not null)
        {
            var ts = current.ToolScore;
            var target = config.ToolExecution.ToolScore;

            if (ts.SuccessDelta.HasValue) target.SuccessDelta = ts.SuccessDelta.Value;
            if (ts.FailDelta.HasValue) target.FailDelta = ts.FailDelta.Value;
            if (ts.WarningThreshold.HasValue) target.WarningThreshold = ts.WarningThreshold.Value;
            if (ts.ScoreMin.HasValue) target.ScoreMin = ts.ScoreMin.Value;
            if (ts.ScoreMax.HasValue) target.ScoreMax = ts.ScoreMax.Value;
            if (ts.DecayRatePerHour.HasValue) target.DecayRatePerHour = ts.DecayRatePerHour.Value;
            if (ts.DecayRecoveryScore.HasValue) target.DecayRecoveryScore = ts.DecayRecoveryScore.Value;
        }

        if (current.BlacklistedTools is not null)
            config.ToolExecution.BlacklistedTools = current.BlacklistedTools;

        if (current.ToolPenalties is not null)
            config.ToolExecution.ToolPenalties = new Dictionary<string, int>(current.ToolPenalties, StringComparer.OrdinalIgnoreCase);
    }

    #endregion
}
