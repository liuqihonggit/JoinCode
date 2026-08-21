namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Vendor, Description = "查看或切换 LLM 供应商", Usage = "/vendor [名称|list]", Category = ChatCommandCategory.Model, ArgumentHint = "[openai|anthropic|deepseek|azure|agnes|sensenova|bedrock|list]")]
public sealed class VendorCommand : ChatCommandBase
{
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();
        var config = context.GetCommandServices().WorkflowConfig;
        if (config is null)
        {
            ChatCommandBase.HandleError("供应商切换", new InvalidOperationException("引擎未就绪：缺少 WorkflowConfig"));
            return ChatCommandResult.Continue();
        }

        var currentVendor = config.Provider?.Vendor ?? VendorKind.OpenAi.ToValue();

        // 无参 / list → 列出全部供应商（VendorKind 枚举为唯一数据源，规则7）并标记当前
        if (string.IsNullOrEmpty(args) || args is "list" or "ls")
        {
            TerminalHelper.WriteLine("=== 可用供应商 ===");
            foreach (var kind in Enum.GetValues<VendorKind>())
            {
                var value = kind.ToValue();
                var marker = value.Equals(currentVendor, StringComparison.OrdinalIgnoreCase) ? " ← 当前" : string.Empty;
                TerminalHelper.WriteLine($"  {value}{marker}");
            }
            return ChatCommandResult.Continue();
        }

        // 校验目标供应商（FromValue 大小写不敏感由枚举值精确匹配保证）
        var target = VendorKindExtensions.FromValue(args);
        if (target is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}未知供应商: {args}。输入 /vendor 查看可用列表{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var targetValue = target.Value.ToValue();
        if (targetValue.Equals(currentVendor, StringComparison.OrdinalIgnoreCase))
        {
            TerminalHelper.WriteLine($"当前已是 {targetValue}，无需切换");
            return ChatCommandResult.Continue();
        }

        // 切换 — 对齐 GUI SetVendorAsync 语义：内存 Vendor + 默认模型跟随 + profile 持久化
        config.Provider!.Vendor = targetValue;

        var catalog = ResolveCatalog(context);
        var defaultModelId = catalog.GetDefaultModelForProvider(targetValue);
        if (!string.IsNullOrEmpty(defaultModelId))
        {
            config.Provider.ModelId = defaultModelId;

            var fastModeService = ChatCommandBase.GetService<IFastModeService>(context, typeof(IFastModeService));
            fastModeService?.SetPrimaryModel(defaultModelId);
        }

        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
        if (configService is not null)
        {
            await configService.SetAsync(ConfigKeyConstants.Profile, targetValue, context.CancellationToken).ConfigureAwait(false);
        }

        TerminalHelper.WriteLine($"已切换供应商: {currentVendor} → {targetValue}" +
            (string.IsNullOrEmpty(defaultModelId) ? string.Empty : $"（默认模型: {defaultModelId}）"));
        return ChatCommandResult.Continue();
    }

    private static IModelCatalog ResolveCatalog(ChatCommandContext context)
    {
        return ChatCommandBase.GetService<IModelCatalog>(context, typeof(IModelCatalog))
            ?? throw new InvalidOperationException("[APP003] 模型目录服务未初始化");
    }
}
