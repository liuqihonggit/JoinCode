
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Assistant, Description = "长期助手模式", Usage = "/assistant [on|off|status]", Category = ChatCommandCategory.Agent)]
public sealed class AssistantCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Assistant;
    public string Description => "长期助手模式";
    public string Usage => "/assistant [on|off|status]";
    public string[] Aliases => [];
    public string ArgumentHint => "[on|off|status]";
    public bool IsHidden => true;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var assistantService = ChatCommandBase.GetService<IAssistantModeService>(context);

        if (assistantService is null)
            return Task.FromResult(ChatCommandResult.Continue());

        // ToggleAction 枚举标准映射 (on/off/status),加上 1/0 别名映射 (ToggleAction 保持通用,不内置别名)
        var arg = context.Arguments?.Trim().ToLowerInvariant();
        var toggle = arg switch
        {
            "1" => ToggleAction.On,
            "0" => ToggleAction.Off,
            _ => ToggleActionExtensions.FromValue(arg)
        };

        switch (toggle)
        {
            case ToggleAction.On:
                HandleOn();
                break;
            case ToggleAction.Off:
                HandleOff();
                break;
            case ToggleAction.Status:
            case null:
                HandleStatus(assistantService);
                break;
            default:
                TerminalHelper.WriteLine(L.T(StringKey.HostAssistantUnknownArg, context.Arguments));
                TerminalHelper.WriteLine(L.T(StringKey.HostAssistantUsage));
                break;
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static void HandleOn()
    {
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AssistantMode, "1");
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantModeEnabled));
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantEnvVarSet, JccEnvVarConstants.AssistantMode));
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantPersistHint));
    }

    private static void HandleOff()
    {
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AssistantMode, "0");
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantModeDisabled));
    }

    private static void HandleStatus(IAssistantModeService assistantService)
    {
        var enabled = assistantService.IsAssistantModeEnabled;
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantModeStatusHeader));
        TerminalHelper.WriteLine(enabled ? L.T(StringKey.HostAssistantStatusEnabled) : L.T(StringKey.HostAssistantStatusDisabled));
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantEnvStatusLabel, JccEnvVarConstants.AssistantMode, Environment.GetEnvironmentVariable(JccEnvVarConstants.AssistantMode) ?? "(未设置)"));
    }
}
