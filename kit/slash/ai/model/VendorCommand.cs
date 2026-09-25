
namespace JoinCode.ChatCommands;

/// <summary>
/// /vendor 命令 — 查看或切换 LLM 供应商
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Vendor, Description = "查看或切换 LLM 供应商", Usage = "/vendor [名称|list]", Category = ChatCommandCategory.Model, ArgumentHint = "[openai|anthropic|deepseek|azure|agnes|sensenova|bedrock|zhipu|jev|list]")]
// Enum 列表委托 VendorKindEnumConstants + CrudActionEnumConstants.List,与 VendorKind 枚举保持同步
[ChatCommandArg("name", Type = "string", Description = "供应商名称或 list", Enum = [VendorKindEnumConstants.OpenAi, VendorKindEnumConstants.Anthropic, VendorKindEnumConstants.DeepSeek, VendorKindEnumConstants.Azure, VendorKindEnumConstants.Agnes, VendorKindEnumConstants.Sensenova, VendorKindEnumConstants.Bedrock, VendorKindEnumConstants.Zhipu, VendorKindEnumConstants.Jev, CrudActionEnumConstants.List])]
public sealed class VendorCommand : ChatCommandBase {
    /// <summary>
    /// 执行供应商命令,无参或 list 时列出全部供应商,否则切换到目标供应商并同步默认模型与持久化配置
    /// </summary>
    /// <param name="context">命令执行上下文,提供参数与工作流配置</param>
    /// <returns>表示命令执行结果的任务,始终返回 Continue 以继续会话</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();
        var config = context.GetCommandServices().WorkflowConfig;
        if (config is null) {
            ChatCommandBase.HandleError("供应商切换", new InvalidOperationException("引擎未就绪：缺少 WorkflowConfig"));
            return ChatCommandResult.Continue();
        }

        var currentVendor = config.Provider?.Vendor ?? VendorKind.OpenAi.ToValue();

        // 无参 / list → 列出全部供应商（VendorKind 枚举为唯一数据源，规则7）并标记当前
        if (string.IsNullOrEmpty(args) || args is "list" or "ls") {
            TerminalHelper.WriteLine("=== 可用供应商 ===");
            foreach (var kind in Enum.GetValues<VendorKind>()) {
                var value = kind.ToValue();
                var marker = value.Equals(currentVendor, StringComparison.OrdinalIgnoreCase) ? " ← 当前" : string.Empty;
                TerminalHelper.WriteLine($"  {value}{marker}");
            }
            return ChatCommandResult.Continue();
        }

        // 校验目标供应商（FromValue 大小写不敏感由枚举值精确匹配保证）
        var target = VendorKindExtensions.FromValue(args);
        if (target is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}未知供应商: {args}。输入 /vendor 查看可用列表{AnsiStyleEnumConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var targetValue = target.Value.ToValue();
        if (targetValue.Equals(currentVendor, StringComparison.OrdinalIgnoreCase)) {
            TerminalHelper.WriteLine($"当前已是 {targetValue}，无需切换");
            return ChatCommandResult.Continue();
        }

        // 切换 — 对齐 GUI SetVendorAsync 语义：内存 Vendor + 默认模型跟随 + profile 持久化
        config.Provider!.Vendor = targetValue;

        var catalog = ResolveCatalog(context);
        var defaultModelId = catalog.GetDefaultModelForProvider(targetValue);
        if (!string.IsNullOrEmpty(defaultModelId)) {
            config.Provider.ModelId = defaultModelId;

            var fastModeService = ChatCommandBase.GetService<IFastModeService>(context, typeof(IFastModeService));
            fastModeService?.SetPrimaryModel(defaultModelId);
        }

        var configService = ChatCommandBase.GetService<IConfigurationService>(context, typeof(IConfigurationService));
        if (configService is not null) {
            await configService.SetAsync(ConfigKeyEnumConstants.Profile, targetValue, context.CancellationToken).ConfigureAwait(false);
        }

        TerminalHelper.WriteLine($"已切换供应商: {currentVendor} → {targetValue}" +
            (string.IsNullOrEmpty(defaultModelId) ? string.Empty : $"（默认模型: {defaultModelId}）"));
        return ChatCommandResult.Continue();
    }

    private static IModelCatalog ResolveCatalog(ChatCommandContext context) {
        return ChatCommandBase.GetService<IModelCatalog>(context, typeof(IModelCatalog))
            ?? throw new InvalidOperationException("[APP003] 模型目录服务未初始化");
    }
}