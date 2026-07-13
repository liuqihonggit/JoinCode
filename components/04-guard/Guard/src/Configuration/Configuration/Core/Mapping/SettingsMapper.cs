
namespace Core.Configuration;

/// <summary>
/// 将 SettingsJson + 环境变量覆盖映射到 WorkflowConfig
/// 优先级: 环境变量 > SettingsJson 字段 > Provider 定义默认值 > 内置默认值
/// </summary>
[Register]
public sealed class SettingsMapper
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
        var envProvider = Environment.GetEnvironmentVariable(JccEnvVar.Provider.ToValue());
        if (!string.IsNullOrEmpty(envProvider) && config.Provider.Provider != envProvider)
        {
            config.Provider.Provider = envProvider;

            // Provider 变更时，重新应用 Provider 定义的默认值
            var newDefinition = _registry.TryGet(envProvider)
                ?? throw new ConfigurationException(
                    $"未知的 Provider '{envProvider}'，可用值: {string.Join(", ", _registry.RegisteredProviders)}。");

            config.Provider.Endpoint ??= newDefinition.DefaultEndpoint;
            config.Provider.Definition = newDefinition;

            // 仅当 ModelId 未被显式设置时，使用新 Provider 的默认模型
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(JccEnvVar.ModelId.ToValue())))
            {
                config.Provider.ModelId = newDefinition.DefaultModelId
                    ?? throw new ConfigurationException(
                        $"Provider '{newDefinition.ProviderName}' 没有定义默认模型，请通过 {JccEnvVar.ModelId.ToValue()} 环境变量指定模型。");
            }
        }

        var envModelId = Environment.GetEnvironmentVariable(JccEnvVar.ModelId.ToValue());
        if (!string.IsNullOrEmpty(envModelId))
            config.Provider.ModelId = envModelId;

        var envEndpoint = Environment.GetEnvironmentVariable(JccEnvVar.Endpoint.ToValue());
        if (!string.IsNullOrEmpty(envEndpoint))
            config.Provider.Endpoint = envEndpoint;

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
        // Provider 优先级: settings.provider > 默认值
        if (!string.IsNullOrEmpty(settings?.Provider))
        {
            config.Provider.Provider = settings.Provider;
        }

        // Endpoint 优先级: settings.endpoint > 默认值
        if (!string.IsNullOrEmpty(settings?.Endpoint))
        {
            config.Provider.Endpoint = settings.Endpoint;
        }

        // Provider 定义自动配置默认值
        var definition = _registry.TryGet(config.Provider.Provider);
        if (definition is not null)
        {
            config.Provider.Endpoint ??= definition.DefaultEndpoint;
            config.Provider.Definition = definition;
        }

        // 模型 ID 优先级: settings.model > Provider 定义默认模型
        if (!string.IsNullOrEmpty(settings?.Model))
        {
            config.Provider.ModelId = settings.Model;
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
                $"未知的 Provider '{config.Provider.Provider}'，可用值: {string.Join(", ", _registry.RegisteredProviders)}。" +
                $"请通过 {JccEnvVar.Provider.ToValue()} 环境变量指定正确的 Provider。");
        }

        // API Version
        config.Provider.ApiVersion ??= definition?.DefaultApiVersion ?? "2024-02-01";
    }

    private static void ApplyCodeExecutionSettings(WorkflowConfig config, SettingsJson? settings)
    {
        if (settings?.Sandbox is null) return;

        if (settings.Sandbox.Enabled.HasValue)
            config.CodeExecution.ReadOnlyFilesystem = settings.Sandbox.Enabled.Value;
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

    #endregion
}
