namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Model, Description = "切换或查看模型", Usage = "/model [model-id|default|info]", Category = ChatCommandCategory.Model, ArgumentHint = "[model-id|default|info]")]
public sealed class ModelCommand : ChatCommandBase
{
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(args))
        {
            return ShowModelPickerAsync(context);
        }

        if (args.Equals("info", StringComparison.OrdinalIgnoreCase) || args == "?")
        {
            return ShowModelInfoAsync(context);
        }

        if (args.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return SwitchToDefaultModelAsync(context);
        }

        return SwitchToModelAsync(context, args);
    }

    private async Task<ChatCommandResult> ShowModelPickerAsync(ChatCommandContext context)
    {
        var fastModeService = ChatCommandBase.GetService<IFastModeService>(context, typeof(IFastModeService));
        var currentModelId = fastModeService?.PrimaryModelId ?? "unknown";
        var provider = GetCurrentProvider(context);
        var catalog = ResolveModelCatalog(context);
        var models = catalog.GetModelsForProvider(provider);
        models = catalog.EnsureCurrentModelInList(models, currentModelId);
        var providerName = catalog.GetProviderDisplayName(provider);
        var effortLevel = context.Services.ExecutionSettingsProvider?.EffortLevel ?? EffortLevel.Auto;
        var isFastModeActive = fastModeService?.IsFastModeActive ?? false;

        // 非交互模式或测试环境回退到文本列表模式
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            TerminalHelper.WriteLine($"=== 模型列表 ({providerName}) ===");
            for (var i = 0; i < models.Length; i++)
            {
                var marker = models[i].Id.Equals(currentModelId, StringComparison.OrdinalIgnoreCase) ? " *" : "";
                TerminalHelper.WriteLine($"  {i + 1}. {models[i].DisplayName}{marker}");
            }
            TerminalHelper.WriteLine($"当前模型: {currentModelId}");
            TerminalHelper.WriteLine($"Effort: {effortLevel.ToValue()} | Fast mode: {(isFastModeActive ? "ON" : "OFF")}");
            return ChatCommandResult.Continue();
        }

        var picker = new ModelPicker();
        var selectedIndex = 0;

        for (var i = 0; i < models.Length; i++)
        {
            if (models[i].Id.Equals(currentModelId, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }

        // 计算渲染行数（模型行 + effort行 + fast mode行 + 快捷键行 + 标题行）
        int GetRenderLineCount()
        {
            var count = models.Length + 2; // 标题 + 快捷键
            if (effortLevel != EffortLevel.Auto) count++;
            if (isFastModeActive) count++;
            return count;
        }

        while (!context.CancellationToken.IsCancellationRequested)
        {
            var output = picker.Render(models, selectedIndex, currentModelId, providerName, effortLevel, isFastModeActive);
            TerminalHelper.WriteRaw(output);

            // 非交互模式或测试环境回退：保持当前模型
            if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                TerminalHelper.NewLine();
                return ChatCommandResult.Continue();
            }
            else
            {
                var key = TerminalHelper.ReadKey(intercept: true);

                TerminalHelper.WriteRaw(AnsiEscape.CursorUp(GetRenderLineCount()));
                TerminalHelper.WriteRaw("\r");
                TerminalHelper.WriteRaw(AnsiControlConstants.ClearScreenFromCursor);

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        if (selectedIndex > 0) selectedIndex--;
                        break;
                    case ConsoleKey.DownArrow:
                        if (selectedIndex < models.Length - 1) selectedIndex++;
                        break;
                    case ConsoleKey.LeftArrow:
                        // ← 切换 effort 等级 — 对齐 TS effort cycling
                        effortLevel = ModelPicker.CycleEffort(effortLevel == EffortLevel.Auto ? EffortLevel.Medium : effortLevel, forward: false);
                        if (context.Services.ExecutionSettingsProvider is not null)
                            context.Services.ExecutionSettingsProvider.EffortLevel = effortLevel;
                        break;
                    case ConsoleKey.RightArrow:
                        // → 切换 effort 等级 — 对齐 TS effort cycling
                        effortLevel = ModelPicker.CycleEffort(effortLevel == EffortLevel.Auto ? EffortLevel.Medium : effortLevel, forward: true);
                        if (context.Services.ExecutionSettingsProvider is not null)
                            context.Services.ExecutionSettingsProvider.EffortLevel = effortLevel;
                        break;
                    case ConsoleKey.Enter:
                        await ApplyModelSwitchAsync(context, models[selectedIndex].Id).ConfigureAwait(false);
                        TerminalHelper.WriteLine($"{TerminalColors.Primary}已切换模型: {models[selectedIndex].DisplayName}{AnsiStyleConstants.Reset}");
                        if (effortLevel != EffortLevel.Auto)
                        {
                            TerminalHelper.WriteLine($"  Effort: {effortLevel.ToValue()}");
                            // 持久化 Picker 中调节的 effort — 对齐 TS resolvePickerEffortPersistence
                            var pickerConfigService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
                            if (pickerConfigService is not null)
                                await pickerConfigService.SetAsync(ConfigKeyConstants.EffortLevel, effortLevel.ToValue(), context.CancellationToken).ConfigureAwait(false);
                        }
                        return ChatCommandResult.Continue();
                    case ConsoleKey.Escape:
                        TerminalHelper.WriteLine($"{TerminalColors.Muted}已取消{AnsiStyleConstants.Reset}");
                        return ChatCommandResult.Continue();
                }
            }
        }

        return ChatCommandResult.Continue();
    }

    private Task<ChatCommandResult> ShowModelInfoAsync(ChatCommandContext context)
    {
        var fastModeService = ChatCommandBase.GetService<IFastModeService>(context, typeof(IFastModeService));
        var executionSettings = context.Services.ExecutionSettingsProvider;
        var provider = GetCurrentProvider(context);
        var catalog = ResolveModelCatalog(context);
        var providerName = catalog.GetProviderDisplayName(provider);

        TerminalHelper.WriteLine("=== 当前模型信息 ===");
        TerminalHelper.WriteLine($"  Provider: {providerName}");
        TerminalHelper.WriteLine($"  主模型: {fastModeService?.PrimaryModelId ?? "unknown"}");
        TerminalHelper.WriteLine($"  快速模型: {fastModeService?.FastModelId ?? "unknown"}");
        TerminalHelper.WriteLine($"  快速模式: {(fastModeService?.IsFastModeActive == true ? "开启" : "关闭")}");
        TerminalHelper.WriteLine($"  Effort 等级: {(executionSettings?.EffortLevel ?? EffortLevel.Auto).ToValue()}");

        var models = catalog.GetModelsForProvider(provider);
        TerminalHelper.WriteLine($"  可用模型数: {models.Length}");

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private async Task<ChatCommandResult> SwitchToDefaultModelAsync(ChatCommandContext context)
    {
        var provider = GetCurrentProvider(context);
        var defaultModel = ResolveModelCatalog(context).GetDefaultModelForProvider(provider);
        await ApplyModelSwitchAsync(context, defaultModel).ConfigureAwait(false);
        TerminalHelper.WriteLine($"{TerminalColors.Primary}已恢复默认模型: {defaultModel}{AnsiStyleConstants.Reset}");

        return ChatCommandResult.Continue();
    }

    private async Task<ChatCommandResult> SwitchToModelAsync(ChatCommandContext context, string modelArg)
    {
        var provider = GetCurrentProvider(context);
        var resolvedModelId = ResolveModelId(context, modelArg, provider);

        await ApplyModelSwitchAsync(context, resolvedModelId).ConfigureAwait(false);
        TerminalHelper.WriteLine($"{TerminalColors.Primary}已切换模型: {resolvedModelId}{AnsiStyleConstants.Reset}");

        return ChatCommandResult.Continue();
    }

    private static string ResolveModelId(ChatCommandContext context, string input, string provider)
    {
        var alias = ResolveModelCatalog(context).ResolveAlias(input, provider);
        if (alias is not null)
            return alias;

        return input;
    }

    private static IModelCatalog ResolveModelCatalog(ChatCommandContext context)
    {
        return ChatCommandBase.GetService<IModelCatalog>(context, typeof(IModelCatalog))
            ?? throw new InvalidOperationException("模型目录服务未初始化");
    }

    private static string GetCurrentProvider(ChatCommandContext context)
    {
        return context.Services.WorkflowConfig?.Provider?.Provider
            ?? Environment.GetEnvironmentVariable(JccEnvVar.Provider.ToValue())
            ?? ProviderKind.OpenAI.ToValue();
    }

    private static async Task ApplyModelSwitchAsync(ChatCommandContext context, string modelId)
    {
        // 1. 更新内存中的模型
        var fastModeService = ChatCommandBase.GetService<IFastModeService>(context, typeof(IFastModeService));
        if (fastModeService is not null)
        {
            fastModeService.SetPrimaryModel(modelId);
        }
        else
        {
            var envVar = JccEnvVar.ModelId.ToValue();
            Environment.SetEnvironmentVariable(envVar, modelId);
        }

        // 2. 持久化到 settings.json — 对齐 TS userSettings
        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
        if (configService is not null)
        {
            await configService.SetAsync("modelId", modelId, context.CancellationToken).ConfigureAwait(false);
        }

        // 3. Fast Mode 自动关闭检查 — 对齐 TS handleFastModeAutoOff
        if (fastModeService is not null && fastModeService.IsFastModeActive)
        {
            var provider = GetCurrentProvider(context);
            if (!ResolveModelCatalog(context).SupportsFastMode(modelId, provider))
            {
                fastModeService.Deactivate();
                TerminalHelper.WriteLine($"{TerminalColors.Warning}模型 {modelId} 不支持快速模式，已自动关闭{AnsiStyleConstants.Reset}");
            }
        }

        // 4. Effort 自动降级检查 — 对齐 TS effortAutoDowngrade
        var settingsProvider = context.Services.ExecutionSettingsProvider;
        if (settingsProvider is not null)
        {
            var currentEffort = settingsProvider.EffortLevel;
            if (currentEffort == EffortLevel.Max)
            {
                var provider = GetCurrentProvider(context);
                if (!ResolveModelCatalog(context).SupportsMaxEffort(modelId, provider))
                {
                    settingsProvider.EffortLevel = EffortLevel.High;
                    TerminalHelper.WriteLine($"{TerminalColors.Warning}模型 {modelId} 不支持 max effort，已降级为 high{AnsiStyleConstants.Reset}");
                }
            }
        }
    }
}
