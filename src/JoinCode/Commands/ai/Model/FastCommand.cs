namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Fast, Description = "切换快速模式（使用更小/更快的模型）", Usage = "/fast [on|off]", Category = ChatCommandCategory.Model)]
public sealed class FastCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Fast;
    public string Description => "切换快速模式（使用更小/更快的模型）";
    public string Usage => "/fast [on|off]";
    public string[] Aliases => [];
    public string ArgumentHint => "[on|off]";
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();
        var fastModeService = ChatCommandBase.GetService<IFastModeService>(context, typeof(IFastModeService));
        var config = context.Services.WorkflowConfig;

        // ToggleAction 枚举标准映射 (on/off/toggle/status),enable/disable/1/0 走别名映射
        var toggle = args switch
        {
            "enable" or "1" => ToggleAction.On,
            "disable" or "0" => ToggleAction.Off,
            _ => ToggleActionExtensions.FromValue(args),
        };

        switch (toggle)
        {
            case ToggleAction.On:
                EnableFastMode(fastModeService, config);
                var fastModel = fastModeService?.FastModelId ?? "fast model";
                TerminalHelper.WriteLine($"快速模式: 已启用 (使用 {fastModel})");
                break;
            case ToggleAction.Off:
                DisableFastMode(fastModeService, config);
                var primaryModel = fastModeService?.PrimaryModelId ?? "primary model";
                TerminalHelper.WriteLine($"快速模式: 已禁用 (使用 {primaryModel})");
                break;
            default:
                var isFast = fastModeService?.IsFastModeActive ?? config?.FastMode ?? false;
                var currentModel = isFast
                    ? fastModeService?.FastModelId ?? "unknown"
                    : fastModeService?.PrimaryModelId ?? "unknown";
                TerminalHelper.WriteLine($"快速模式: {(isFast ? "已启用" : "已禁用")} (当前模型: {currentModel})");
                TerminalHelper.WriteLine("使用 /fast on 启用，/fast off 禁用");
                break;
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static void EnableFastMode(IFastModeService? fastModeService, WorkflowConfig? config)
    {
        if (fastModeService is not null)
        {
            fastModeService.Activate();
            return;
        }

        if (config is not null)
        {
            config.FastMode = true;
        }
    }

    private static void DisableFastMode(IFastModeService? fastModeService, WorkflowConfig? config)
    {
        if (fastModeService is not null)
        {
            fastModeService.Deactivate();
            return;
        }

        if (config is not null)
        {
            config.FastMode = false;
        }
    }
}
