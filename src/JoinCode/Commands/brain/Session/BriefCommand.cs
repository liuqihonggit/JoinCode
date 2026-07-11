
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Brief, Description = "切换简要消息模式", Usage = "/brief [on|off]", Category = ChatCommandCategory.Session)]
public sealed class BriefCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Brief;
    public string Description => "切换简要消息模式";
    public string Usage => "/brief [on|off]";
    public string[] Aliases => [];
    public string ArgumentHint => "[on|off]";
    public bool IsHidden => false;

    /// <summary>
    /// 命令是否当前可用 — 对齐 TS brief.ts isEnabled()
    /// TS: isEnabled = getBriefConfig().enable_slash_command (GrowthBook远程配置)
    /// CS: 开源项目无远程配置，直接读取 JCC_BRIEF 环境变量（与 EntitlementService.IsBriefEntitled 逻辑一致）
    /// 当 JCC_BRIEF=0/false 时命令不可见，对齐 TS 编译时 DCE 效果
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            var envValue = Environment.GetEnvironmentVariable(JccEnvVarConstants.Brief);
            if (!string.IsNullOrEmpty(envValue))
            {
                return !envValue.Equals("0", StringComparison.OrdinalIgnoreCase)
                    && !envValue.Equals("false", StringComparison.OrdinalIgnoreCase);
            }
            return true; // 默认允许
        }
    }

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var briefModeService = context.Services!.BriefModeService;

        if (briefModeService is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}简要模式服务不可用{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var entitlementService = ChatCommandBase.GetService<IEntitlementService>(context, typeof(IEntitlementService));

        var args = ChatCommandBase.GetNormalizedArgs(context);
        var previousState = briefModeService.IsEnabled;
        var toggle = ToggleActionExtensions.FromValue(args);
        var newState = toggle switch
        {
            ToggleAction.On => (bool?)true,
            ToggleAction.Off => (bool?)false,
            _ => null,
        };

        // 对齐 TS: Entitlement check only gates the on-transition — off is always allowed
        if (newState == true || (newState is null && !briefModeService.IsEnabled))
        {
            // 开启时检查 entitlement 权限
            if (entitlementService is not null && !entitlementService.IsBriefEntitled)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Muted}简要模式未启用 — 当前账户无权限{AnsiStyleConstants.Reset}");
                return ChatCommandResult.Continue();
            }
        }

        if (newState == true)
        {
            briefModeService.Enable();
        }
        else if (newState == false)
        {
            briefModeService.Disable();
        }
        else
        {
            briefModeService.Toggle();
        }

        PrintStatus(briefModeService);

        // 注入 system-reminder 提醒 LLM 模式变更（对标 TS brief.ts）
        if (previousState != briefModeService.IsEnabled)
        {
            await InjectBriefStateReminderAsync(context, briefModeService.IsEnabled).ConfigureAwait(false);
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 注入 system-reminder 提醒 LLM Brief 模式状态变更
    /// </summary>
    private static async Task InjectBriefStateReminderAsync(ChatCommandContext context, bool isEnabled)
    {
        var reminderManager = ChatCommandBase.GetService<Core.Prompts.SystemReminderManager>(context, typeof(Core.Prompts.SystemReminderManager));

        if (reminderManager is null) return;

        var toolName = SystemToolNameConstants.SendUserMessage;
        var content = isEnabled
            ? $"Brief mode is now enabled. Use the {toolName} tool for all user-facing output. This tool allows you to send messages directly to the user along with optional file attachments. Always prefer using this tool over plain text responses when brief mode is active."
            : $"Brief mode is now disabled. The {toolName} tool is no longer available. Resume using normal text responses for all user-facing output.";

        await reminderManager.AddReminderAsync("brief-mode-state", content, priority: 10).ConfigureAwait(false);
    }

    private static void PrintStatus(IBriefModeService service)
    {
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
    }
}
