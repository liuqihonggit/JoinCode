namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Fast, Description = "切换快速模式（使用更小/更快的模型）", Usage = "/fast [on|off]", Category = ChatCommandCategory.Model)]
public sealed class FastCommand : ToggleCommandBase
{
    public override string Name => ChatCommandNameConstants.Fast;
    public override string Description => "切换快速模式（使用更小/更快的模型）";
    public override string Usage => "/fast [on|off]";

    protected override ToggleAction? ResolveToggleAction(string args)
    {
        var lower = args.ToLowerInvariant();
        return lower switch
        {
            "enable" or "1" => ToggleAction.On,
            "disable" or "0" => ToggleAction.Off,
            _ => ToggleActionExtensions.FromValue(args),
        };
    }

    protected override ToggleNullAction NullAction => ToggleNullAction.Status;

    protected override Task OnEnabledAsync(ChatCommandContext context)
    {
        var fastModeService = GetService<IFastModeService>(context, typeof(IFastModeService));
        var config = context.Services.WorkflowConfig;

        if (fastModeService is not null)
        {
            fastModeService.Activate();
        }
        else if (config is not null)
        {
            config.FastMode = true;
        }

        var fastModel = fastModeService?.FastModelId ?? "fast model";
        TerminalHelper.WriteLine($"快速模式: 已启用 (使用 {fastModel})");

        return Task.CompletedTask;
    }

    protected override Task OnDisabledAsync(ChatCommandContext context)
    {
        var fastModeService = GetService<IFastModeService>(context, typeof(IFastModeService));
        var config = context.Services.WorkflowConfig;

        if (fastModeService is not null)
        {
            fastModeService.Deactivate();
        }
        else if (config is not null)
        {
            config.FastMode = false;
        }

        var primaryModel = fastModeService?.PrimaryModelId ?? "primary model";
        TerminalHelper.WriteLine($"快速模式: 已禁用 (使用 {primaryModel})");

        return Task.CompletedTask;
    }

    protected override Task PrintStatusAsync(ChatCommandContext context)
    {
        var fastModeService = GetService<IFastModeService>(context, typeof(IFastModeService));
        var config = context.Services.WorkflowConfig;
        var isFast = fastModeService?.IsFastModeActive ?? config?.FastMode ?? false;
        var currentModel = isFast
            ? fastModeService?.FastModelId ?? "unknown"
            : fastModeService?.PrimaryModelId ?? "unknown";
        TerminalHelper.WriteLine($"快速模式: {(isFast ? "已启用" : "已禁用")} (当前模型: {currentModel})");
        TerminalHelper.WriteLine("使用 /fast on 启用，/fast off 禁用");

        return Task.CompletedTask;
    }
}
