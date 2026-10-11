namespace JoinCode.Entry;

/// <summary>
/// 供应商配置中间件 — 配置无效时展示供应商菜单
/// </summary>
[Register(typeof(IMiddleware<StartupContext>), ServiceLifetime.Singleton)]
internal sealed partial class ProviderSetupStep : ServiceEntity, IMiddleware<StartupContext> {
    private readonly IProviderDefinitionRegistry _registry;
    private readonly IConsoleOutput _console;

    /// <summary>
    /// 构造供应商配置中间件 — 注入供应商注册表和控制台输出
    /// </summary>
    /// <param name="registry">供应商定义注册表</param>
    /// <param name="console">控制台输出（用于掩码读取 API Key）</param>
    public ProviderSetupStep(IProviderDefinitionRegistry registry, IConsoleOutput console) {
        _registry = registry;
        _console = console;
    }

    /// <summary>
    /// 中间件入口 — 检测 API Key 是否已配置，未配置时展示供应商菜单引导用户完成配置后调用下一环节
    /// </summary>
    /// <param name="context">启动上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct) {
        context.HasApiKey = !string.IsNullOrEmpty(context.Config.Provider.ApiKey);
        if (context.HasApiKey) {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var (configured, newConfig) = await ShowProviderMenuAsync(context.Config, context.FileSystem, ct).ConfigureAwait(false);
        context.Config = newConfig;
        if (!configured) return;  // 短路

        context.HasApiKey = true;
        await next(context, ct).ConfigureAwait(false);
    }

    private async Task<(bool Success, WorkflowConfig Config)> ShowProviderMenuAsync(WorkflowConfig config, IFileSystem fs, CancellationToken ct) {
        while (true) {
            Cli.TerminalHelper.NewLine();
            Cli.TerminalHelper.WriteLine("═══════════════════════════════════════");
            Cli.TerminalHelper.WriteLine("  JoinCode - AI 智能体命令行工具");
            Cli.TerminalHelper.WriteLine("═══════════════════════════════════════");
            Cli.TerminalHelper.NewLine();
            Cli.TerminalHelper.WriteLine("  未检测到 API Key，请选择供应商配置:");
            Cli.TerminalHelper.NewLine();

            var providers = _registry.GetRegisteredProviders()
                .Select(p => _registry.TryGet(p))
                .OfType<IProviderDefinition>()
                .ToList();

            for (var i = 0; i < providers.Count; i++) {
                Cli.TerminalHelper.WriteLine($"  {i + 1}. {providers[i].DisplayName}");
            }

            var exitIdx = providers.Count + 1;
            Cli.TerminalHelper.WriteLine($"  {exitIdx}. 退出");
            Cli.TerminalHelper.NewLine();
            Cli.TerminalHelper.WriteRaw($"  请选择 [1-{exitIdx}]: ");

            if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) {
                Cli.TerminalHelper.WriteLine("非交互环境，跳过配置。");
                return (false, config);
            }

            var choice = Cli.TerminalHelper.ReadLine()?.Trim();

            if (int.TryParse(choice, out var idx) && idx >= 1 && idx <= providers.Count) {
                config = await ConfigureProviderAsync(config, fs, providers[idx - 1].ProviderName).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(config.Provider.ApiKey)) return (true, config);
                continue;
            }

            if (int.TryParse(choice, out var num) && num == exitIdx) return (false, config);
            if (string.IsNullOrEmpty(choice)) return (false, config);

            Cli.TerminalHelper.WriteLine($"  无效选择，请输入 1-{exitIdx}。");
        }
    }

    private async Task<WorkflowConfig> ConfigureProviderAsync(WorkflowConfig config, IFileSystem fs, string provider) {
        var definition = _registry.TryGet(provider);
        var displayName = definition?.DisplayName ?? provider;
        var envVarHint = definition?.ApiKeyEnvironmentVariable is not null
            ? $"（也可通过环境变量 {definition.ApiKeyEnvironmentVariable} 设置）"
            : "";

        Cli.TerminalHelper.NewLine();
        // 使用 WriteRaw（不换行）以便 ReadPassword 的掩码 * 显示在同一行
        Cli.TerminalHelper.WriteRaw($"请粘贴 {displayName} 的 API Key{envVarHint}（直接回车退出）: ");

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) return config;

        // 使用掩码输入隐藏 API Key 明文 — 复用 IConsoleOutput.ReadPassword 基础设施
        // 决策: 注入 IConsoleOutput 而非直接 Console.ReadKey，保持可测试性（NoOp/Mock 模式可替换）
        // 替代方案已否决: 直接调用 System.Console.ReadKey（不可测试，与 PhysicalConsoleOutput 重复实现）
        var apiKey = _console.ReadPassword(string.Empty);
        if (string.IsNullOrWhiteSpace(apiKey)) return config;

        apiKey = apiKey.Trim();

        // 多态：通过 IProviderDefinition.RequiresInteractiveEndpoint 消除 `if (provider == "azure")` 硬编码
        // Azure 覆写为 true + EndpointPromptText + EndpointRequiredMessage；其余 Provider 默认 false（空实现）
        var newEndpoint = config.Provider.Endpoint;
        if (definition?.RequiresInteractiveEndpoint == true) {
            Cli.TerminalHelper.WriteLine();
            Cli.TerminalHelper.WriteLine(definition.EndpointPromptText ?? "请输入 Endpoint:");
            var endpoint = Cli.TerminalHelper.ReadLine();

            if (string.IsNullOrWhiteSpace(endpoint)) {
                Cli.TerminalHelper.WriteLine($"  {definition.EndpointRequiredMessage ?? "Endpoint 必填，配置已取消。"}");
                return config;
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _)) {
                Cli.TerminalHelper.WriteLine($"  Endpoint '{endpoint}' 不是有效的 URI 格式，配置已取消。");
                return config;
            }

            await ConfigLoader.SaveSettingToSettingsJsonAsync("endpoint", endpoint.Trim(), fs).ConfigureAwait(false);
            newEndpoint = endpoint.Trim();
        }

        // definition 非空时 Endpoint 回退默认值
        if (definition is not null && newEndpoint is null)
            newEndpoint = definition.DefaultEndpoint;

        // 一次性构造新 ProviderConfig + WorkflowConfig，无锁替换引用
        var newProvider = config.Provider with {
            Vendor = provider,
            ApiKey = apiKey,
            Endpoint = newEndpoint,
            Definition = definition ?? config.Provider.Definition,
            Protocol = definition is not null ? definition.Protocol.ToValue() : config.Provider.Protocol,
            ModelId = definition?.DefaultModelId ?? config.Provider.ModelId,
        };

        await ConfigLoader.SaveApiKeyToJccAsync(provider, apiKey, fs).ConfigureAwait(false);
        await ConfigLoader.SaveSettingToSettingsJsonAsync("provider", provider, fs).ConfigureAwait(false);

        Cli.TerminalHelper.WriteLine("API Key 已保存。");
        return config with { Provider = newProvider };
    }
}