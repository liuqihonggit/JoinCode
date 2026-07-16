namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Brief, Description = "切换简要消息模式", Usage = "/brief [on|off]", Category = ChatCommandCategory.Session)]
public sealed class BriefCommand : ToggleCommandBase
{
    public override string Name => ChatCommandNameConstants.Brief;
    public override string Description => "切换简要消息模式";
    public override string Usage => "/brief [on|off]";

    public override bool IsEnabled
    {
        get
        {
            var envValue = Environment.GetEnvironmentVariable(JccEnvVarConstants.Brief);
            if (!string.IsNullOrEmpty(envValue))
            {
                return !envValue.Equals("0", StringComparison.OrdinalIgnoreCase)
                    && !envValue.Equals("false", StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }
    }

    protected override async Task OnEnabledAsync(ChatCommandContext context)
    {
        var briefModeService = context.Services.BriefModeService;
        if (briefModeService is null) return;

        var entitlementService = GetService<IEntitlementService>(context, typeof(IEntitlementService));
        if (entitlementService is not null && !entitlementService.IsBriefEntitled)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}简要模式未启用 — 当前账户无权限{AnsiStyleConstants.Reset}");
            return;
        }

        var previousState = briefModeService.IsEnabled;
        briefModeService.Enable();
        await PrintStatusAsync(context).ConfigureAwait(false);

        if (previousState != briefModeService.IsEnabled)
        {
            await InjectBriefStateReminderAsync(context, briefModeService.IsEnabled).ConfigureAwait(false);
        }
    }

    protected override async Task OnDisabledAsync(ChatCommandContext context)
    {
        var briefModeService = context.Services.BriefModeService;
        if (briefModeService is null) return;

        var previousState = briefModeService.IsEnabled;
        briefModeService.Disable();
        await PrintStatusAsync(context).ConfigureAwait(false);

        if (previousState != briefModeService.IsEnabled)
        {
            await InjectBriefStateReminderAsync(context, briefModeService.IsEnabled).ConfigureAwait(false);
        }
    }

    protected override async Task OnToggleAsync(ChatCommandContext context)
    {
        var briefModeService = context.Services.BriefModeService;
        if (briefModeService is null) return;

        var entitlementService = GetService<IEntitlementService>(context, typeof(IEntitlementService));

        if (!briefModeService.IsEnabled)
        {
            if (entitlementService is not null && !entitlementService.IsBriefEntitled)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Muted}简要模式未启用 — 当前账户无权限{AnsiStyleConstants.Reset}");
                return;
            }
        }

        var previousState = briefModeService.IsEnabled;
        briefModeService.Toggle();
        await PrintStatusAsync(context).ConfigureAwait(false);

        if (previousState != briefModeService.IsEnabled)
        {
            await InjectBriefStateReminderAsync(context, briefModeService.IsEnabled).ConfigureAwait(false);
        }
    }

    protected override Task PrintStatusAsync(ChatCommandContext context)
    {
        var service = context.Services.BriefModeService;
        if (service is null) return Task.CompletedTask;

        if (service.IsEnabled)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Primary}简要消息模式已启用{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"  LLM 将通过 SendUserMessage 工具回复用户");
            if (service.EnabledAt.HasValue)
            {
                TerminalHelper.WriteLine($"  启用时间: {service.EnabledAt.Value:yyyy-MM-dd HH:mm:ss}");
            }
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}简要消息模式已禁用 - LLM 将使用普通文本回复{AnsiStyleConstants.Reset}");
        }

        return Task.CompletedTask;
    }

    private static async Task InjectBriefStateReminderAsync(ChatCommandContext context, bool isEnabled)
    {
        var reminderManager = GetService<Core.Prompts.SystemReminderManager>(context, typeof(Core.Prompts.SystemReminderManager));
        if (reminderManager is null) return;

        var toolName = SystemToolNameConstants.SendUserMessage;
        var content = isEnabled
            ? $"Brief mode is now enabled. Use the {toolName} tool for all user-facing output. This tool allows you to send messages directly to the user along with optional file attachments. Always prefer using this tool over plain text responses when brief mode is active."
            : $"Brief mode is now disabled. The {toolName} tool is no longer available. Resume using normal text responses for all user-facing output.";

        await reminderManager.AddReminderAsync("brief-mode-state", content, priority: 10).ConfigureAwait(false);
    }
}
