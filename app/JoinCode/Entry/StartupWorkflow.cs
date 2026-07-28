namespace JoinCode.Entry;

internal static class StartupWorkflow
{
    /// <summary>
    /// 确保全局配置文件存在 — 首次启动时自动创建带注释的模板，不覆盖已有文件
    /// 必须在 LoadConfigAsync 之前调用，确保配置文件可供加载
    /// </summary>
    internal static async Task EnsureConfigFilesExistAsync(IFileSystem fs)
    {
        var appDataPath = WorkflowConstants.Paths.JccDirectory;
        var settingsPath = Path.Combine(appDataPath, AppDataConstants.SettingsFileName);
        var authPath = Path.Combine(appDataPath, AppDataConstants.AuthFileName);

        if (!fs.DirectoryExists(appDataPath))
        {
            DirectoryHelper.EnsureDirectoryExists(fs, appDataPath);
        }

        if (!fs.FileExists(settingsPath))
        {
            await fs.WriteAllTextAsync(settingsPath, BuildDefaultSettingsTemplate()).ConfigureAwait(false);
        }

        if (!fs.FileExists(authPath))
        {
            await fs.WriteAllTextAsync(authPath, BuildDefaultAuthTemplate()).ConfigureAwait(false);
        }
    }

    internal static async Task RunOnboardingIfNeededAsync(IOnboardingService onboardingService, CommandLineOptions options, IFileSystem fs, bool hasApiKey, IProviderDefinitionRegistry registry, WorkflowConfig? config = null)
    {
        await onboardingService.InitializeAsync();

        if (onboardingService.IsOnboardingComplete)
        {
            return;
        }

        if (options.TrustWorkspace)
        {
            await onboardingService.CompleteAsync();
            return;
        }

        if (hasApiKey)
        {
            await onboardingService.CompleteAsync();
            return;
        }

        await onboardingService.StartAsync();

        while (!onboardingService.IsOnboardingComplete)
        {
            var state = onboardingService.CurrentState;

            if (state.CurrentStep == OnboardingStep.ApiKey)
            {
                var (success, errorMessage) = await PromptAndSaveProviderConfigAsync(config, fs, registry);
                if (!success && !string.IsNullOrEmpty(errorMessage))
                {
                    Cli.TerminalHelper.WriteLine();
                    Cli.TerminalHelper.WriteLine(errorMessage);
                    Cli.TerminalHelper.WriteLine("按任意键继续...");
                    if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
                    {
                        Cli.TerminalHelper.ReadKey(intercept: true);
                    }
                    await onboardingService.CompleteAsync();
                    return;
                }
                else if (!success)
                {
                    await onboardingService.CompleteAsync();
                    return;
                }
                await onboardingService.NextStepAsync();
                continue;
            }

            // 简化版 Onboarding 显示 — 纯文本，无 TUI 渲染
            Cli.TerminalHelper.WriteLine($"[{state.CurrentStepIndex + 1}/{state.TotalSteps}] {state.CurrentStep}");
            Cli.TerminalHelper.WriteLine("按 Enter 继续，Esc 跳过");

            if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                await onboardingService.CompleteAsync();
                break;
            }
            else
            {
                var key = Cli.TerminalHelper.ReadKey(intercept: true);

                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        if (state.CurrentStep == OnboardingStep.Complete || state.CurrentStepIndex >= state.TotalSteps - 1)
                        {
                            await onboardingService.CompleteAsync();
                        }
                        else
                        {
                            await onboardingService.NextStepAsync();
                        }
                        break;
                    case ConsoleKey.Escape:
                        await onboardingService.SkipAsync();
                        break;
                }
            }
        }
    }

    /// <summary>
    /// 统一的 Provider 配置提示和保存逻辑
    /// </summary>
    internal static async Task<(bool Success, string? ErrorMessage)> PromptAndSaveProviderConfigAsync(WorkflowConfig? config, IFileSystem fs, IProviderDefinitionRegistry registry)
    {
        var hint = $"首次使用需要配置 API Key，输入后将保存到 ~/{AppDataConstants.AppDataFolder}/{AppDataConstants.AuthFileName}";
        var provider = ProviderPicker.Show("openai", "未检测到 API Key。", hint, registry);
        if (string.IsNullOrEmpty(provider))
        {
            return (false, null);
        }

        var definition = registry.TryGet(provider);
        var displayName = definition?.DisplayName ?? provider;
        var envVarHint = definition?.ApiKeyEnvironmentVariable is not null
            ? $"（也可通过环境变量 {definition.ApiKeyEnvironmentVariable} 设置）"
            : "";

        Cli.TerminalHelper.WriteLine();
        Cli.TerminalHelper.WriteLine($"请粘贴 {displayName} 的 API Key{envVarHint}（直接回车退出）: ");

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            return (false, null);
        }

        var apiKey = Cli.TerminalHelper.ReadLine();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (false, null);
        }

        apiKey = apiKey.Trim();

        // 多态：通过 IProviderDefinition.RequiresInteractiveEndpoint 消除 `if (provider == "azure")` 硬编码
        // Azure 覆写为 true + EndpointPromptText + EndpointRequiredMessage；其余 Provider 默认 false
        string? endpoint = null;
        if (definition?.RequiresInteractiveEndpoint == true)
        {
            Cli.TerminalHelper.WriteLine();
            Cli.TerminalHelper.WriteLine(definition.EndpointPromptText ?? "请输入 Endpoint:");
            Cli.TerminalHelper.WriteLine(definition.EndpointRequiredMessage ?? "Endpoint 必填。");
            endpoint = Cli.TerminalHelper.ReadLine();

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return (false, definition.EndpointRequiredMessage ?? "Endpoint 必填，配置已取消。");
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                return (false, $"Endpoint '{endpoint}' 不是有效的 URI 格式，请输入有效的 URI 地址。");
            }

            endpoint = endpoint.Trim();
        }

        // 多态：通过 IProviderDefinition.SerializeAuthCredentials 序列化凭证
        // Azure 覆写为 JSON 对象（含 endpoint + apiKey）；其余 Provider 默认直接返回 apiKey
        var credentials = definition?.SerializeAuthCredentials(apiKey, endpoint) ?? apiKey;

        // 保存配置到内存
        if (config is not null)
        {
            config.Provider.Provider = provider;

            // 多态：通过 IProviderDefinition.IsCompoundAuthFormat + ExtractApiKeyFromCompound 解析复合格式
            if (definition is not null && definition.IsCompoundAuthFormat(credentials))
            {
                var extractedKey = definition.ExtractApiKeyFromCompound(credentials);
                config.Provider.ApiKey = extractedKey ?? credentials;
            }
            else
            {
                config.Provider.ApiKey = credentials;
            }

            if (endpoint is not null)
            {
                config.Provider.Endpoint = endpoint;
            }

            if (definition is not null)
            {
                config.Provider.Definition = definition;
                config.Provider.ModelId = definition.DefaultModelId;
                config.Provider.Endpoint ??= definition.DefaultEndpoint;
            }
        }

        // 多态：通过 IProviderDefinition.IsCompoundAuthFormat + ExtractApiKeyFromCompound 保存到文件
        if (definition is not null && definition.IsCompoundAuthFormat(credentials))
        {
            var extractedKey = definition.ExtractApiKeyFromCompound(credentials);
            await ConfigLoader.SaveApiKeyToJccAsync(provider, extractedKey ?? credentials, fs);
            if (endpoint is not null)
            {
                await ConfigLoader.SaveSettingToSettingsJsonAsync("endpoint", endpoint, fs);
            }
        }
        else
        {
            await ConfigLoader.SaveApiKeyToJccAsync(provider, credentials, fs);
        }

        await ConfigLoader.SaveSettingToSettingsJsonAsync("provider", provider, fs);

        return (true, null);
    }

    internal static async Task<bool> CheckWorkspaceTrustAsync(CommandLineOptions options, IFileSystem fs)
    {
        var workspacePath = fs.GetCurrentDirectory();
        var trustManager = new TrustFolderManager(fs);

        if (trustManager.IsTrusted(workspacePath))
        {
            return true;
        }

        if (options.TrustWorkspace)
        {
            trustManager.Trust(workspacePath);
            return true;
        }

        // 纯 CLI 模式的信任确认 — 简单的 y/n 提示
        Cli.TerminalHelper.WriteLine($"工作目录 {workspacePath} 尚未被信任。");
        Cli.TerminalHelper.WriteRaw("是否信任此目录? (y/N): ");

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            Cli.TerminalHelper.WriteLine("非交互环境，拒绝信任。");
            return false;
        }

        var response = Cli.TerminalHelper.ReadLine();
        if (response?.ToLowerInvariant() == "y")
        {
            trustManager.Trust(workspacePath);
            return true;
        }

        return false;
    }

    private static string BuildDefaultSettingsTemplate()
    {
        var defaultModel = JoinCode.Abstractions.Configuration.Llm.ModelConfigLoader.GetDefaultModelId("deepseek");
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  // LLM Provider: deepseek | openai | anthropic | azure | agnes");
        sb.AppendLine("  \"provider\": \"deepseek\",");
        sb.AppendLine();
        sb.AppendLine("  // 模型 ID，可用 /model 命令切换");
        sb.AppendLine($"  \"model\": \"{defaultModel}\",");
        sb.AppendLine();
        sb.AppendLine("  // API 端点（默认使用 Provider 内置地址，无需修改）");
        sb.AppendLine("  // \"endpoint\": \"https://api.deepseek.com\",");
        sb.AppendLine();
        sb.AppendLine("  // 推理努力级别: low | medium | high");
        sb.AppendLine("  // \"effortLevel\": \"high\",");
        sb.AppendLine();
        sb.AppendLine("  // 快速模式：使用轻量模型处理简单任务");
        sb.AppendLine("  // \"fastMode\": false,");
        sb.AppendLine();
        sb.AppendLine("  // 默认 Shell: powershell | bash | cmd");
        sb.AppendLine("  // \"defaultShell\": \"powershell\",");
        sb.AppendLine();
        sb.AppendLine("  // 语言: zh-CN | en-US");
        sb.AppendLine("  // \"language\": \"zh-CN\",");
        sb.AppendLine();
        sb.AppendLine("  // 自动记忆：自动将重要信息保存到记忆文件");
        sb.AppendLine("  // \"autoMemoryEnabled\": true");
        sb.Append('}');
        return sb.ToString();
    }

    private static string BuildDefaultAuthTemplate()
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  // API Key 存储 — 键名为 Provider 名称，值为对应的 API Key");
        sb.AppendLine("  // 支持的键名: deepseek | openai | anthropic | azure | agnes");
        sb.AppendLine("  //");
        sb.AppendLine("  // 示例:");
        sb.AppendLine("  // \"deepseek\": \"sk-your-deepseek-api-key\",");
        sb.AppendLine("  // \"openai\": \"sk-your-openai-api-key\",");
        sb.AppendLine("  // \"anthropic\": \"sk-ant-your-anthropic-api-key\"");
        sb.AppendLine("  //");
        sb.AppendLine("  // 也可通过环境变量设置（优先级更高）:");
        sb.AppendLine("  //   DEEPSEEK_API_KEY / OPENAI_API_KEY / ANTHROPIC_API_KEY / AZURE_OPENAI_API_KEY");
        sb.Append('}');
        return sb.ToString();
    }
}
