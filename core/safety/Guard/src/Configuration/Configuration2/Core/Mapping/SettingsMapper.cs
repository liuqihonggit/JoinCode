
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

        // Provider 配置
        ApplyProviderSettings(config, settings);

        // 代码执行配置
        ApplyCodeExecutionSettings(config, settings);

        // Worktree 配置
        ApplyWorktreeSettings(config, settings);

        // 快速模式
        config.FastMode = settings?.FastMode ?? false;

        // 工具评分配置
        ApplyToolScoreSettings(config, settings);

        return config;
    }

    /// <summary>
    /// 应用环境变量覆盖到已映射的 WorkflowConfig
    /// 环境变量优先级最高，覆盖所有文件配置
    /// 注意: API Key 不在此处理，由 ConfigLoader.ResolveApiKeyAsync 统一解析
    /// </summary>
    public void ApplyEnvOverrides(WorkflowConfig config)
    {
        // Provider 环境变量覆盖
        var envProvider = Environment.GetEnvironmentVariable(JccEnvVar.Vendor.ToValue());
        if (!string.IsNullOrEmpty(envProvider) && config.Provider.Vendor != envProvider)
        {
            config.Provider.Vendor = envProvider;

            // --vendor 自动匹配 profiles 中的同名预设，应用其 model/endpoint
            ApplyProfileFromVendor(envProvider, config);

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

        // JCC_PROTOCOL 环境变量覆盖 — 允许显式指定协议，覆盖从 Vendor 定义推导的协议
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

        // JCC_PROFILE 已由 EnvOverrideApplier 在 SettingsJson 层覆盖 CurrentProfile

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

        // Provider 定义的端点环境变量覆盖（API Key 由 ResolveApiKeyAsync 统一处理）
        ApplyProviderDefinitionEndpointEnvOverrides(config);

        var envStateFilePath = Environment.GetEnvironmentVariable(JccEnvVar.StateFilePath.ToValue());
        if (!string.IsNullOrEmpty(envStateFilePath))
            config.StateFilePath = envStateFilePath;
    }

    /// <summary>
    /// 从 SettingsJson 的 env 字段注入环境变量到当前进程
    /// 对齐 TS 版: settings.env 中的键值对会注入到子进程环境变量
    /// </summary>
    public static void InjectEnvFromSettings(SettingsJson? settings)
    {
        if (settings?.Env is null) return;

        foreach (var (key, value) in settings.Env)
        {
            // 不覆盖已存在的环境变量（优先级: 系统环境变量 > settings.env）
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    /// <summary>
    /// 合并两个 SettingsJson（低优先级 + 高优先级）— 委托给源码生成器自动生成的 SettingsJson.Merge
    /// </summary>
    public static SettingsJson Merge(SettingsJson? baseSettings, SettingsJson? overrideSettings)
        => SettingsJson.Merge(baseSettings, overrideSettings);

    #region 内部方法

    private void ApplyProviderSettings(WorkflowConfig config, SettingsJson? settings)
    {
        // Profile 覆盖 — currentProfile 指向 profiles 字典中的档案，其 provider/model/endpoint 覆盖顶层字段
        var effectiveSettings = ApplyProfileOverride(settings);

        // Provider 优先级: settings.provider > 默认值
        if (!string.IsNullOrEmpty(effectiveSettings?.Provider))
        {
            config.Provider.Vendor = effectiveSettings.Provider;
        }

        // Endpoint 优先级: settings.endpoint > 默认值
        if (!string.IsNullOrEmpty(effectiveSettings?.Endpoint))
        {
            config.Provider.Endpoint = effectiveSettings.Endpoint;
        }

        // Provider 定义自动配置默认值
        var definition = _registry.TryGet(config.Provider.Vendor);
        if (definition is not null)
        {
            config.Provider.Endpoint ??= definition.DefaultEndpoint;
            config.Provider.Definition = definition;
            config.Provider.Protocol = definition.Protocol.ToValue();
        }

        // 模型 ID 优先级: settings.model > Provider 定义默认模型
        if (!string.IsNullOrEmpty(effectiveSettings?.Model))
        {
            config.Provider.ModelId = effectiveSettings.Model;
        }
        else if (definition is not null)
        {
            config.Provider.ModelId = definition.DefaultModelId
                ?? throw new ConfigurationException(
                    $"Provider '{definition.ProviderName}' 没有定义默认模型，请通过 settings.model 或 {JccEnvVar.ModelId.ToValue()} 环境变量指定模型。");
        }
        else
        {
            throw new ConfigurationException(
                $"未知的 Provider '{config.Provider.Vendor}'，可用值: {string.Join(", ", _registry.RegisteredProviders)}。" +
                $"请通过 {JccEnvVar.Vendor.ToValue()} 环境变量指定正确的 Provider。");
        }

        // API Version
        config.Provider.ApiVersion ??= definition?.DefaultApiVersion ?? "2024-02-01";
    }

    /// <summary>
    /// 应用 Profile 覆盖 — 如果 currentProfile 指向 profiles 中的档案，
    /// 用档案的 provider/model/endpoint 覆盖顶层字段（Profile 优先级高于顶层字段）
    /// </summary>
    private static SettingsJson? ApplyProfileOverride(SettingsJson? settings)
    {
        if (settings is null || string.IsNullOrEmpty(settings.CurrentProfile))
            return settings;

        if (settings.Vendor is null || !settings.Vendor.TryGetValue(settings.CurrentProfile, out var profile))
            return settings;

        // Profile 中的字段覆盖顶层字段（非 null 的才覆盖）
        return new SettingsJson
        {
            Schema = settings.Schema,
            Provider = profile.Provider ?? settings.Provider,
            Model = profile.Model ?? settings.Model,
            Endpoint = profile.Endpoint ?? settings.Endpoint,
            EffortLevel = settings.EffortLevel,
            DefaultShell = settings.DefaultShell,
            FastMode = settings.FastMode,
            Language = settings.Language,
            AutoMemoryEnabled = settings.AutoMemoryEnabled,
            AutoDreamEnabled = settings.AutoDreamEnabled,
            ShowThinkingSummaries = settings.ShowThinkingSummaries,
            Env = settings.Env,
            Permissions = settings.Permissions,
            Hooks = settings.Hooks,
            McpServers = settings.McpServers,
            Sandbox = settings.Sandbox,
            EnabledPlugins = settings.EnabledPlugins,
            ApiKeyHelper = settings.ApiKeyHelper,
            RespectGitignore = settings.RespectGitignore,
            CleanupPeriodDays = settings.CleanupPeriodDays,
            IncludeCoAuthoredBy = settings.IncludeCoAuthoredBy,
            IncludeGitInstructions = settings.IncludeGitInstructions,
            AvailableModels = settings.AvailableModels,
            ModelOverrides = settings.ModelOverrides,
            EnableAllProjectMcpServers = settings.EnableAllProjectMcpServers,
            EnabledMcpjsonServers = settings.EnabledMcpjsonServers,
            DisabledMcpjsonServers = settings.DisabledMcpjsonServers,
            DisableAllHooks = settings.DisableAllHooks,
            Worktree = settings.Worktree,
            ActiveWorktreeSession = settings.ActiveWorktreeSession,
            StatusLine = settings.StatusLine,
            OutputStyle = settings.OutputStyle,
            ToolScore = settings.ToolScore,
            BlacklistedTools = settings.BlacklistedTools,
            ToolPenalties = settings.ToolPenalties,
            CustomHyperedges = settings.CustomHyperedges,
            SearchScope = settings.SearchScope,
            Vendor = settings.Vendor,
            CurrentProfile = settings.CurrentProfile,
        };
    }

    private static void ApplyCodeExecutionSettings(WorkflowConfig config, SettingsJson? settings)
    {
        if (settings?.Sandbox is null) return;

        if (settings.Sandbox.Enabled.HasValue)
            config.CodeExecution.ReadOnlyFilesystem = settings.Sandbox.Enabled.Value;

        if (settings.Sandbox.RestrictNetwork.HasValue)
            config.CodeExecution.AllowNetworkAccess = !settings.Sandbox.RestrictNetwork.Value;

        if (settings.Sandbox.MemoryLimitMb.HasValue && settings.Sandbox.MemoryLimitMb.Value > 0)
            config.CodeExecution.MaxMemoryMB = settings.Sandbox.MemoryLimitMb.Value;

        if (settings.Sandbox.AllowedPaths is not null && settings.Sandbox.AllowedPaths.Count > 0)
            config.CodeExecution.AllowedDirectories = string.Join(";", settings.Sandbox.AllowedPaths);
    }

    private static void ApplyWorktreeSettings(WorkflowConfig config, SettingsJson? settings)
    {
        if (settings?.Worktree is null) return;

        if (settings.Worktree.SparsePaths is not null)
            config.Worktree.SparsePaths = settings.Worktree.SparsePaths;

        if (settings.Worktree.SymlinkDirectories is not null)
            config.Worktree.SymlinkDirectories = settings.Worktree.SymlinkDirectories;
    }

    private static void ApplyProviderDefinitionEndpointEnvOverrides(WorkflowConfig config)
    {
        if (config.Provider.Definition is not { } definition) return;

        var envEndpoint = definition.ResolveEndpointFromEnv();
        if (!string.IsNullOrEmpty(envEndpoint))
            config.Provider.Endpoint = envEndpoint;
    }

    /// <summary>
    /// --vendor 自动匹配 profiles 中的同名预设 — 从 settings.json 读取 profiles，
    /// 找到与 vendor 同名的 profile 时，应用其 model/endpoint（仅覆盖未显式设置的值）
    /// </summary>
    private static void ApplyProfileFromVendor(string vendor, WorkflowConfig config)
    {
        var fs = new IO.FileSystem.PhysicalFileSystem();
        var settings = ConfigLoader.LoadSettingsJsonAsync(fs).GetAwaiter().GetResult();
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
        if (settings?.ToolScore is null) return;

        var ts = settings.ToolScore;
        var target = config.ToolExecution.ToolScore;

        if (ts.SuccessDelta.HasValue) target.SuccessDelta = ts.SuccessDelta.Value;
        if (ts.FailDelta.HasValue) target.FailDelta = ts.FailDelta.Value;
        if (ts.WarningThreshold.HasValue) target.WarningThreshold = ts.WarningThreshold.Value;
        if (ts.ScoreMin.HasValue) target.ScoreMin = ts.ScoreMin.Value;
        if (ts.ScoreMax.HasValue) target.ScoreMax = ts.ScoreMax.Value;
        if (ts.DecayRatePerHour.HasValue) target.DecayRatePerHour = ts.DecayRatePerHour.Value;
        if (ts.DecayRecoveryScore.HasValue) target.DecayRecoveryScore = ts.DecayRecoveryScore.Value;

        if (settings.BlacklistedTools is not null)
            config.ToolExecution.BlacklistedTools = settings.BlacklistedTools;

        if (settings.ToolPenalties is not null)
            config.ToolExecution.ToolPenalties = new Dictionary<string, int>(settings.ToolPenalties, StringComparer.OrdinalIgnoreCase);
    }

    #endregion
}
