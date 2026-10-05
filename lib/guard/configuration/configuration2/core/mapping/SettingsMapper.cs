
namespace Core.Configuration;

/// <summary>
/// 将 SettingsJson + 环境变量覆盖映射到 WorkflowConfig
/// 优先级: 环境变量 > SettingsJson 字段 > Provider 定义默认值 > 内置默认值
/// </summary>
[Register(typeof(SettingsMapper), ServiceLifetime.Singleton)]
public sealed partial class SettingsMapper : ServiceEntity {
    private readonly IProviderDefinitionRegistry _registry;

    /// <summary>
    /// 跳过 Provider 存在性验证 — 元命令模式（mcp_list/slash_call 等）不需要 LLM 服务，允许未知 Provider 降级运行
    /// </summary>
    public bool SkipProviderValidation { get; set; }

    /// <summary>构造函数 — 注入 Provider 定义注册表</summary>
    public SettingsMapper(IProviderDefinitionRegistry registry) {
        _registry = registry;
    }

    /// <summary>
    /// 将 SettingsJson 映射到 WorkflowConfig，并应用环境变量覆盖
    /// </summary>
    public WorkflowConfig ToWorkflowConfig(SettingsJson? settings) {
        var config = new WorkflowConfig();

        config = ApplyProviderSettings(config, settings);
        config = ApplyCodeExecutionSettings(config, settings);
        config = ApplyWorktreeSettings(config, settings);
        config = config with { FastMode = settings?.Current?.FastMode ?? false };
        config = config with { ShellExecution = config.ShellExecution with { IsAntiCharLossConfirm = settings?.Current?.IsAntiCharLossConfirm ?? false } };
        config = ApplyToolScoreSettings(config, settings);
        config = ApplySubAgentConcurrencySettings(config, settings);
        config = ApplySubAgentLivenessSettings(config, settings);
        config = ApplyActorSettings(config, settings);

        return config;
    }

    /// <summary>
    /// 应用环境变量覆盖到已映射的 WorkflowConfig
    /// 环境变量优先级最高，覆盖所有文件配置
    /// 注意: API Key 不在此处理，由 ConfigLoader.ResolveApiKeyAsync 统一解析
    /// </summary>
    public async Task<WorkflowConfig> ApplyEnvOverridesAsync(WorkflowConfig config, SettingsJson? settings = null) {
        var envProvider = Environment.GetEnvironmentVariable(JccEnvVar.Vendor.ToValue());
        if (!string.IsNullOrEmpty(envProvider) && config.Provider.Vendor != envProvider) {
            config = config with { Provider = config.Provider with { Vendor = envProvider } };

            config = await ApplyProfileFromVendorAsync(envProvider, config, settings).ConfigureAwait(false);

            var newDefinition = _registry.TryGet(envProvider);
            if (newDefinition is null && !SkipProviderValidation) {
                throw new ConfigurationException(
                    $"未知的 Provider '{envProvider}'，可用值: {string.Join(", ", _registry.GetRegisteredProviders())}。");
            }

            if (newDefinition is not null) {
                config = await ApplyProviderDefinitionDefaultsAsync(config, newDefinition, envProvider, settings).ConfigureAwait(false);
            } else {
                Diag.WriteLifecycle($"[WARN] 跳过 Provider 验证 — 未知 Provider '{envProvider}'，可用值: {string.Join(", ", _registry.GetRegisteredProviders())}。元命令模式降级运行。");
            }
        }

        var envProtocol = Environment.GetEnvironmentVariable(JccEnvVar.Protocol.ToValue());
        if (!string.IsNullOrEmpty(envProtocol))
            config = config with { Provider = config.Provider with { Protocol = envProtocol } };

        var envOrgId = Environment.GetEnvironmentVariable(JccEnvVar.OrganizationId.ToValue());
        if (!string.IsNullOrEmpty(envOrgId))
            config = config with { Provider = config.Provider with { OrganizationId = envOrgId } };

        var envApiVersion = Environment.GetEnvironmentVariable(JccEnvVar.ApiVersion.ToValue());
        if (!string.IsNullOrEmpty(envApiVersion))
            config = config with { Provider = config.Provider with { ApiVersion = envApiVersion } };

        var envOAuth = Environment.GetEnvironmentVariable(JccEnvVar.EnableOAuth.ToValue());
        if (bool.TryParse(envOAuth, out var enableOAuth))
            config = config with { Provider = config.Provider with { EnableOAuthTokenSupport = enableOAuth } };

        var envTimeout = Environment.GetEnvironmentVariable(JccEnvVar.CodeExecutionTimeout.ToValue());
        if (int.TryParse(envTimeout, out var timeout))
            config = config with { CodeExecution = config.CodeExecution with { ExecutionTimeoutSeconds = timeout } };

        var envMaxMemory = Environment.GetEnvironmentVariable(JccEnvVar.CodeExecutionMaxMemory.ToValue());
        if (int.TryParse(envMaxMemory, out var maxMemory))
            config = config with { CodeExecution = config.CodeExecution with { MaxMemoryMB = maxMemory } };

        config = ApplyProviderDefinitionEndpointEnvOverrides(config);

        var envStateFilePath = Environment.GetEnvironmentVariable(JccEnvVar.StateFilePath.ToValue());
        if (!string.IsNullOrEmpty(envStateFilePath))
            config = config with { StateFilePath = envStateFilePath };

        return config;
    }

    /// <summary>
    /// 应用新 Provider 定义的默认值到 WorkflowConfig — Endpoint/Definition/Protocol/ModelId 回退
    /// </summary>
    private async Task<WorkflowConfig> ApplyProviderDefinitionDefaultsAsync(WorkflowConfig config, IProviderDefinition newDefinition, string envProvider, SettingsJson? settings) {
        var provider = config.Provider;
        provider = provider with { Endpoint = provider.Endpoint ?? newDefinition.DefaultEndpoint, Definition = newDefinition };
        var profileProtocol = await GetProfileProtocolAsync(envProvider, settings).ConfigureAwait(false);
        if (string.IsNullOrEmpty(profileProtocol))
            provider = provider with { Protocol = newDefinition.Protocol.ToValue() };

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(JccEnvVar.ModelId.ToValue()))) {
            provider = provider with { ModelId = provider.ModelId ?? newDefinition.DefaultModelId };
            if (provider.ModelId is null && !SkipProviderValidation) {
                throw new ConfigurationException(
                    $"Provider '{newDefinition.ProviderName}' 没有定义默认模型，请通过 {JccEnvVar.ModelId.ToValue()} 环境变量指定模型。");
            }
        }
        return config with { Provider = provider };
    }

    /// <summary>
    /// 从 SettingsJson 的 env 字段注入环境变量到当前进程
    /// </summary>
    public static void InjectEnvFromSettings(SettingsJson? settings) {
        if (settings?.Current?.Env is null) return;

        foreach (var (key, value) in settings.Current.Env) {
            if (Environment.GetEnvironmentVariable(key) is null) {
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

    private WorkflowConfig ApplyProviderSettings(WorkflowConfig config, SettingsJson? settings) {
        var current = settings?.Current;
        var profile = settings?.GetActiveProfile();
        var provider = config.Provider;

        if (profile is not null) {
            if (!string.IsNullOrEmpty(profile.Provider))
                provider = provider with { Vendor = profile.Provider };
            else if (!string.IsNullOrEmpty(current?.Profile))
                provider = provider with { Vendor = current.Profile };
        } else if (!string.IsNullOrEmpty(current?.Profile)) {
            provider = provider with { Vendor = current.Profile };
        }

        if (!string.IsNullOrEmpty(profile?.Endpoint))
            provider = provider with { Endpoint = profile.Endpoint };

        var definition = _registry.TryGet(provider.Vendor);
        if (definition is not null) {
            provider = provider with { Endpoint = provider.Endpoint ?? definition.DefaultEndpoint, Definition = definition };
            if (!string.IsNullOrEmpty(profile?.Protocol))
                provider = provider with { Protocol = profile.Protocol };
            else
                provider = provider with { Protocol = definition.Protocol.ToValue() };
        }

        if (!string.IsNullOrEmpty(profile?.Model)) {
            provider = provider with { ModelId = profile.Model };
        } else if (definition is not null) {
            provider = provider with { ModelId = definition.DefaultModelId };
            if (provider.ModelId is null && !SkipProviderValidation) {
                throw new ConfigurationException(
                    $"Provider '{definition.ProviderName}' 没有定义默认模型，请通过 vendor[current.profile].model 或 {JccEnvVar.ModelId.ToValue()} 环境变量指定模型。");
            }
        } else if (SkipProviderValidation) {
            Diag.WriteLifecycle($"[WARN] 跳过 Provider 验证 — 未知 Provider '{provider.Vendor}'，可用值: {string.Join(", ", _registry.GetRegisteredProviders())}。元命令模式降级运行。");
        } else {
            throw new ConfigurationException(
                $"未知的 Provider '{provider.Vendor}'，可用值: {string.Join(", ", _registry.GetRegisteredProviders())}。" +
                $"请通过 {JccEnvVar.Vendor.ToValue()} 环境变量指定正确的 Provider。");
        }

        config = config with { Provider = provider };

        if (!string.IsNullOrEmpty(current?.Profile))
            config = config with { CurrentProfile = current.Profile };

        config = config with { Provider = config.Provider with { ApiVersion = config.Provider.ApiVersion ?? definition?.DefaultApiVersion ?? "2024-02-01" } };

        return config;
    }

    private static WorkflowConfig ApplyCodeExecutionSettings(WorkflowConfig config, SettingsJson? settings) {
        var sandbox = settings?.Current?.Sandbox;
        if (sandbox is null) return config;

        var ce = config.CodeExecution;
        if (sandbox.Enabled.HasValue)
            ce = ce with { ReadOnlyFilesystem = sandbox.Enabled.Value };
        if (sandbox.RestrictNetwork.HasValue)
            ce = ce with { AllowNetworkAccess = !sandbox.RestrictNetwork.Value };
        if (sandbox.MemoryLimitMb.HasValue && sandbox.MemoryLimitMb.Value > 0)
            ce = ce with { MaxMemoryMB = sandbox.MemoryLimitMb.Value };
        if (sandbox.AllowedPaths is not null && sandbox.AllowedPaths.Count > 0)
            ce = ce with { AllowedDirectories = string.Join(";", sandbox.AllowedPaths) };

        return config with { CodeExecution = ce };
    }

    private static WorkflowConfig ApplyWorktreeSettings(WorkflowConfig config, SettingsJson? settings) {
        var worktree = settings?.Current?.Worktree;
        if (worktree is null) return config;

        var wt = config.Worktree;
        if (worktree.SparsePaths is not null)
            wt = wt with { SparsePaths = worktree.SparsePaths };
        if (worktree.SymlinkDirectories is not null)
            wt = wt with { SymlinkDirectories = worktree.SymlinkDirectories };

        return config with { Worktree = wt };
    }

    private static WorkflowConfig ApplyProviderDefinitionEndpointEnvOverrides(WorkflowConfig config) {
        if (config.Provider.Definition is not { } definition) return config;

        var envEndpoint = definition.ResolveEndpointFromEnv();
        if (!string.IsNullOrEmpty(envEndpoint))
            return config with { Provider = config.Provider with { Endpoint = envEndpoint } };
        return config;
    }

    /// <summary>
    /// --vendor 自动匹配 vendor 字典中的同名预设
    /// </summary>
    private static async Task<WorkflowConfig> ApplyProfileFromVendorAsync(string vendor, WorkflowConfig config, SettingsJson? settings) {
        if (settings is null) {
            var settingsPath = Path.Combine(AppDataConstants.Paths.JccDirectory, AppDataConstants.SettingsFileName);
            if (BclFileIO.Instance.FileExists(settingsPath)) {
                settings = await DirtyReadRetry.ReadWithRetryAsync(
                    () => Task.FromResult(BclFileIO.Instance.ReadAllText(settingsPath)),
                    json => RelaxedJsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson),
                    settingsPath).ConfigureAwait(false);
            }
        }

        if (settings?.Vendor is null || !settings.Vendor.TryGetValue(vendor, out var profile))
            return config;

        var provider = config.Provider;
        if (!string.IsNullOrEmpty(profile.Model) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(JccEnvVar.ModelId.ToValue())))
            provider = provider with { ModelId = profile.Model };
        if (!string.IsNullOrEmpty(profile.Endpoint))
            provider = provider with { Endpoint = profile.Endpoint };
        if (!string.IsNullOrEmpty(profile.Protocol))
            provider = provider with { Protocol = profile.Protocol };

        return config with { Provider = provider, CurrentProfile = vendor };
    }

    /// <summary>从 settings.json 的 vendor 节点读取指定供应商的 protocol 配置</summary>
    private static async Task<string?> GetProfileProtocolAsync(string vendor, SettingsJson? settings) {
        if (settings is null) {
            var settingsPath = Path.Combine(AppDataConstants.Paths.JccDirectory, AppDataConstants.SettingsFileName);
            if (BclFileIO.Instance.FileExists(settingsPath)) {
                settings = await DirtyReadRetry.ReadWithRetryAsync(
                    () => Task.FromResult(BclFileIO.Instance.ReadAllText(settingsPath)),
                    json => RelaxedJsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson),
                    settingsPath).ConfigureAwait(false);
            }
        }

        if (settings?.Vendor is null || !settings.Vendor.TryGetValue(vendor, out var profile))
            return null;
        return profile.Protocol;
    }

    private static WorkflowConfig ApplyToolScoreSettings(WorkflowConfig config, SettingsJson? settings) {
        var current = settings?.Current;
        if (current is null) return config;

        var te = config.ToolExecution;
        var ts = current.ToolScore;
        var oldScore = te.ToolScore;

        var newTe = te with {
            ToolScore = oldScore with {
                SuccessDelta = ts?.SuccessDelta ?? oldScore.SuccessDelta,
                FailDelta = ts?.FailDelta ?? oldScore.FailDelta,
                WarningThreshold = ts?.WarningThreshold ?? oldScore.WarningThreshold,
                ScoreMin = ts?.ScoreMin ?? oldScore.ScoreMin,
                ScoreMax = ts?.ScoreMax ?? oldScore.ScoreMax,
                DecayRatePerHour = ts?.DecayRatePerHour ?? oldScore.DecayRatePerHour,
                DecayRecoveryScore = ts?.DecayRecoveryScore ?? oldScore.DecayRecoveryScore,
            },
            BlacklistedTools = current.BlacklistedTools ?? te.BlacklistedTools,
            ToolPenalties = current.ToolPenalties is not null
                ? new Dictionary<string, int>(current.ToolPenalties, StringComparer.OrdinalIgnoreCase)
                : te.ToolPenalties,
        };
        return config with { ToolExecution = newTe };
    }

    /// <summary>
    /// 映射子代理并发控制配置 — spawn/execute/fork 三阶段上限（ADR 0048）
    /// </summary>
    private static WorkflowConfig ApplySubAgentConcurrencySettings(WorkflowConfig config, SettingsJson? settings) {
        var sub = settings?.Current?.SubAgentConcurrency;
        if (sub is null) return config;

        return config with {
            SubAgentConcurrency = config.SubAgentConcurrency with {
                MaxConcurrentSpawns = sub.MaxConcurrentSpawns,
                MaxConcurrentExecutions = sub.MaxConcurrentExecutions,
                MaxConcurrentForks = sub.MaxConcurrentForks
            }
        };
    }

    /// <summary>
    /// 映射子代理卡死防护配置 — 纵深防御四层参数（ADR 0106）
    /// </summary>
    private static WorkflowConfig ApplySubAgentLivenessSettings(WorkflowConfig config, SettingsJson? settings) {
        var sub = settings?.Current?.SubAgentLiveness;
        if (sub is null) return config;

        return config with {
            SubAgentLiveness = config.SubAgentLiveness with {
                AgentTimeoutSeconds = sub.AgentTimeoutSeconds,
                IdleThresholdSeconds = sub.IdleThresholdSeconds,
                CompletionCheckThreshold = sub.CompletionCheckThreshold,
                ConfirmationWindowSeconds = sub.ConfirmationWindowSeconds,
                ScanIntervalSeconds = sub.ScanIntervalSeconds,
                ChainStallThreshold = sub.ChainStallThreshold,
                PoolMaxSize = sub.PoolMaxSize,
                PoolIdleTimeoutSeconds = sub.PoolIdleTimeoutSeconds,
                PreemptMinWindowRatio = sub.PreemptMinWindowRatio,
                ActivationRecoverySeconds = sub.ActivationRecoverySeconds
            }
        };
    }

    /// <summary>
    /// 映射 Actor 模型配置 — 编译队列模式 + 背压预设(ADR 0074)
    /// 缺失时用默认值(串行模式 + 四档预设),不抛异常。
    /// </summary>
    private static WorkflowConfig ApplyActorSettings(WorkflowConfig config, SettingsJson? settings) {
        var actor = settings?.Current?.Actor;
        if (actor is null) return config;

        var buildQueue = config.Actor.BuildQueue with {
            Mode = actor.BuildQueue.Mode,
            WorkerCount = actor.BuildQueue.WorkerCount,
            CrossProcessLockPath = actor.BuildQueue.CrossProcessLockPath
        };

        var bp = config.Actor.Backpressure;
        var newBp = bp with {
            CodingAgentTask = CopyPreset(bp.CodingAgentTask, actor.Backpressure.CodingAgentTask),
            LlmGateway = CopyPreset(bp.LlmGateway, actor.Backpressure.LlmGateway),
            Router = CopyPreset(bp.Router, actor.Backpressure.Router),
            Build = CopyPreset(bp.Build, actor.Backpressure.Build)
        };

        var newActor = config.Actor with { BuildQueue = buildQueue, Backpressure = newBp };
        newActor.Validate();
        return config with { Actor = newActor };
    }

    private static BackpressurePreset CopyPreset(BackpressurePreset target, BackpressurePreset source) {
        return target with {
            Capacity = source.Capacity,
            HighWatermark = source.HighWatermark,
            CriticalWatermark = source.CriticalWatermark,
            SendTimeoutSeconds = source.SendTimeoutSeconds
        };
    }

    #endregion
}