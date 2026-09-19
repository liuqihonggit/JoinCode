namespace JoinCode.ChatCommands;

/// <summary>
/// /brief 命令 — 切换简要消息模式，启用后 LLM 通过 SendUserMessage 工具回复用户。
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Brief, Description = "切换简要消息模式", Usage = "/brief [on|off]", Category = ChatCommandCategory.Session)]
[ChatCommandArg("state", Type = "string", Description = "开关状态", Enum = new[] { "on", "off" })]
public sealed class BriefCommand : ToggleCommandBase
{
    /// <summary>命令名称。</summary>
    public override string Name => ChatCommandNameEnumConstants.Brief;
    /// <summary>命令描述。</summary>
    public override string Description => "切换简要消息模式";
    /// <summary>命令用法提示。</summary>
    public override string Usage => "/brief [on|off]";

    /// <summary>
    /// 获取简要模式当前是否启用，优先读取环境变量覆盖值。
    /// </summary>
    public override bool IsEnabled
    {
        get
        {
            var envValue = Environment.GetEnvironmentVariable(JccEnvVarEnumConstants.Brief);
            if (!string.IsNullOrEmpty(envValue))
            {
                return !envValue.Equals("0", StringComparison.OrdinalIgnoreCase)
                    && !envValue.Equals("false", StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }
    }

    /// <summary>
    /// 启用简要消息模式时的处理逻辑，含权限校验和状态提醒注入。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected override async Task OnEnabledAsync(ChatCommandContext context)
    {
        var briefModeService = context.GetCommandServices().BriefModeService;
        if (briefModeService is null) return;

        var entitlementService = GetService<IEntitlementService>(context, typeof(IEntitlementService));
        if (entitlementService is not null && !entitlementService.IsBriefEntitled)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}简要模式未启用 — 当前账户无权限{AnsiStyleEnumConstants.Reset}");
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

    /// <summary>
    /// 停用简要消息模式时的处理逻辑，含状态提醒注入。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected override async Task OnDisabledAsync(ChatCommandContext context)
    {
        var briefModeService = context.GetCommandServices().BriefModeService;
        if (briefModeService is null) return;

        var previousState = briefModeService.IsEnabled;
        briefModeService.Disable();
        await PrintStatusAsync(context).ConfigureAwait(false);

        if (previousState != briefModeService.IsEnabled)
        {
            await InjectBriefStateReminderAsync(context, briefModeService.IsEnabled).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 切换简要消息模式时的处理逻辑，含权限校验和状态提醒注入。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected override async Task OnToggleAsync(ChatCommandContext context)
    {
        var briefModeService = context.GetCommandServices().BriefModeService;
        if (briefModeService is null) return;

        var entitlementService = GetService<IEntitlementService>(context, typeof(IEntitlementService));

        if (!briefModeService.IsEnabled)
        {
            if (entitlementService is not null && !entitlementService.IsBriefEntitled)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Muted}简要模式未启用 — 当前账户无权限{AnsiStyleEnumConstants.Reset}");
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

    /// <summary>
    /// 输出简要消息模式当前启用状态及提示信息。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected override Task PrintStatusAsync(ChatCommandContext context)
    {
        var service = context.GetCommandServices().BriefModeService;
        if (service is null) return Task.CompletedTask;

        if (service.IsEnabled)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Primary}简要消息模式已启用{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine($"  LLM 将通过 {SystemToolNameEnumConstants.SendUserMessage} 工具回复用户");
            if (service.EnabledAt.HasValue)
            {
                TerminalHelper.WriteLine($"  启用时间: {service.EnabledAt.Value:yyyy-MM-dd HH:mm:ss}");
            }
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}简要消息模式已禁用 - LLM 将使用普通文本回复{AnsiStyleEnumConstants.Reset}");
        }

        return Task.CompletedTask;
    }

    private static async Task InjectBriefStateReminderAsync(ChatCommandContext context, bool isEnabled)
    {
        var reminderManager = GetService<Core.Prompts.SystemReminderManager>(context, typeof(Core.Prompts.SystemReminderManager));
        if (reminderManager is null) return;

        var toolName = SystemToolNameEnumConstants.SendUserMessage;
        var content = isEnabled
            ? $"Brief mode is now enabled. Use the {toolName} tool for all user-facing output. This tool allows you to send messages directly to the user along with optional file attachments. Always prefer using this tool over plain text responses when brief mode is active."
            : $"Brief mode is now disabled. The {toolName} tool is no longer available. Resume using normal text responses for all user-facing output.";

        await reminderManager.AddReminderAsync("brief-mode-state", content, priority: 10).ConfigureAwait(false);
    }
}
