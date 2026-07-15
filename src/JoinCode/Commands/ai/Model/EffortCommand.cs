namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Effort, Description = "调整推理力度", Usage = "/effort [low|medium|high|max|auto|unset]", Category = ChatCommandCategory.Model)]
public sealed class EffortCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Effort;
    public string Description => "调整推理力度";
    public string Usage => "/effort [low|medium|high|max|auto|unset]";
    public string[] Aliases => [];
    public string ArgumentHint => "[low|medium|high|max|auto|unset]";
    public bool IsHidden => false;

    // 对齐 TS: COMMON_HELP_ARGS
    // 对齐 TS: current/status 关键字

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();
        var statusBar = context.Services.StatusBarData;
        var settingsProvider = context.Services.ExecutionSettingsProvider;
        var fastModeService = ChatCommandBase.GetService<IFastModeService>(context, typeof(IFastModeService));

        // 对齐 TS: help/-h/--help 显示详细帮助
        if (args is "help" or "-h" or "--help")
        {
            ShowHelp();
            return ChatCommandResult.Continue();
        }

        // 对齐 TS: current/status 显示当前effort
        if (string.IsNullOrEmpty(args) || args is "current" or "status")
        {
            ShowCurrentEffort(statusBar, settingsProvider);
            return ChatCommandResult.Continue();
        }

        // 对齐 TS: auto 和 unset 都清除设置
        if (args is "auto" or "unset")
        {
            UpdateEffort(statusBar, settingsProvider, null);
            var envWarning = GetEnvOverrideWarning();
            TerminalHelper.WriteLine(string.IsNullOrEmpty(envWarning)
                ? "推理力度: auto (使用模型默认级别)"
                : $"推理力度: auto (使用模型默认级别)\n{envWarning}");

            await PersistEffortAsync(context, null).ConfigureAwait(false);
            return ChatCommandResult.Continue();
        }

        var effort = EffortLevelHelper.ParseEffortLevel(args) ?? EffortLevelHelper.ParseNumericAlias(args);

        if (effort is null)
        {
            // 对齐 TS: 无效参数提示
            TerminalHelper.WriteLine($"{TerminalColors.Error}无效参数: {args}。有效选项: low, medium, high, max, auto{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        if (effort == EffortLevel.Max)
        {
            // max effort 仅限 Opus 级别模型 — 对齐 TS modelSupportsMaxEffort
            var currentModel = fastModeService?.PrimaryModelId
                ?? Environment.GetEnvironmentVariable(JccEnvVar.ModelId.ToValue())
                ?? "unknown";
            var provider = context.Services.WorkflowConfig?.Provider?.Provider
                ?? Environment.GetEnvironmentVariable(JccEnvVar.Provider.ToValue())
                ?? ProviderKind.OpenAI.ToValue();

            if (!ResolveModelCatalog(context).SupportsMaxEffort(currentModel, provider))
            {
                // 自动降级为 high — 对齐 TS effortAutoDowngrade
                UpdateEffort(statusBar, settingsProvider, EffortLevel.High);
                TerminalHelper.WriteLine($"{TerminalColors.Warning}模型 {currentModel} 不支持 max effort，已降级为 high{AnsiStyleConstants.Reset}");
                await PersistEffortAsync(context, EffortLevel.High).ConfigureAwait(false);
                return ChatCommandResult.Continue();
            }
        }

        var description = GetEffortDescription(effort.Value);
        // 对齐 TS: session-only 标记（如果无法持久化则标记为 this session only）
        var sessionOnly = await IsSessionOnlyAsync(context).ConfigureAwait(false);
        var suffix = sessionOnly ? " (仅本次会话)" : "";
        var envWarn = GetEnvOverrideWarning();
        UpdateEffort(statusBar, settingsProvider, effort.Value);
        TerminalHelper.WriteLine($"推理力度: {effort.Value.ToValue()} ({description}){suffix}");
        if (!string.IsNullOrEmpty(envWarn))
            TerminalHelper.WriteLine(envWarn);

        await PersistEffortAsync(context, effort).ConfigureAwait(false);
        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 对齐 TS: 显示详细帮助文本
    /// </summary>
    private static void ShowHelp()
    {
        TerminalHelper.WriteLine("用法: /effort [low|medium|high|max|auto|unset]");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("推理力度级别:");
        TerminalHelper.WriteLine("  low    - 快速，简洁实现");
        TerminalHelper.WriteLine("  medium - 平衡，标准测试");
        TerminalHelper.WriteLine("  high   - 全面，广泛测试");
        TerminalHelper.WriteLine("  max    - 最深推理，仅 Opus 级别模型");
        TerminalHelper.WriteLine("  auto   - 使用模型默认级别");
        TerminalHelper.WriteLine("  unset  - 清除自定义设置，恢复默认");
    }

    /// <summary>
    /// 对齐 TS: showCurrentEffort — 显示当前effort级别
    /// </summary>
    private static void ShowCurrentEffort(StatusBarData? statusBar, IExecutionSettingsProvider? settingsProvider)
    {
        var current = settingsProvider?.EffortLevel ?? statusBar?.EffortLevel ?? EffortLevel.Auto;
        var description = GetEffortDescription(current);
        var envWarning = GetEnvOverrideWarning();

        if (current == EffortLevel.Auto)
        {
            TerminalHelper.WriteLine($"当前推理力度: auto ({description})");
        }
        else
        {
            TerminalHelper.WriteLine($"当前推理力度: {current.ToValue()} ({description})");
        }

        if (!string.IsNullOrEmpty(envWarning))
            TerminalHelper.WriteLine(envWarning);

        TerminalHelper.WriteLine("使用 /effort low|medium|high|max|auto 调整");
    }

    /// <summary>
    /// 对齐 TS: getEffortEnvOverride — 检查 JCC_EFFORT_LEVEL 环境变量覆盖
    /// </summary>
    private static string GetEnvOverrideWarning()
    {
        var envValue = Environment.GetEnvironmentVariable("JCC_EFFORT_LEVEL");
        if (string.IsNullOrEmpty(envValue)) return "";

        return $"{TerminalColors.Warning}环境变量 JCC_EFFORT_LEVEL={envValue} 将覆盖本次会话的 effort 设置{AnsiStyleConstants.Reset}";
    }

    /// <summary>
    /// 对齐 TS: toPersistableEffort — 判断是否为 session-only
    /// </summary>
    private static async Task<bool> IsSessionOnlyAsync(ChatCommandContext context)
    {
        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
        return configService is null;
    }

    private static async Task PersistEffortAsync(ChatCommandContext context, EffortLevel? effort)
    {
        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
        if (configService is null) return;

        if (effort is null or EffortLevel.Auto)
        {
            await configService.RemoveAsync(ConfigKeyConstants.EffortLevel, context.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            await configService.SetAsync(ConfigKeyConstants.EffortLevel, effort.Value.ToValue(), context.CancellationToken).ConfigureAwait(false);
        }
    }

    private static void UpdateEffort(StatusBarData? statusBar, IExecutionSettingsProvider? settingsProvider, EffortLevel? level)
    {
        var effectiveLevel = level ?? EffortLevel.Auto;
        if (statusBar is not null)
        {
            statusBar.EffortLevel = effectiveLevel;
        }
        if (settingsProvider is not null)
        {
            settingsProvider.EffortLevel = effectiveLevel;
        }
    }

    private static IModelCatalog ResolveModelCatalog(ChatCommandContext context)
    {
        return ChatCommandBase.GetService<IModelCatalog>(context, typeof(IModelCatalog))
            ?? throw new InvalidOperationException("模型目录服务未初始化");
    }

    private static string GetEffortDescription(EffortLevel level) => level switch
    {
        EffortLevel.Low => "快速，简洁实现",
        EffortLevel.Medium => "平衡，标准测试",
        EffortLevel.High => "全面，广泛测试",
        EffortLevel.Max => "最深推理，仅 Opus 级别",
        _ => "模型默认级别"
    };
}
