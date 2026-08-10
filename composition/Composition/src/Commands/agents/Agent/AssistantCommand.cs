namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Assistant, Description = "长期助手模式", Usage = "/assistant [on|off|status]", Category = ChatCommandCategory.Agent)]
public sealed class AssistantCommand : ToggleCommandBase
{
    public override string Name => ChatCommandNameConstants.Assistant;
    public override string Description => "长期助手模式";
    public override string Usage => "/assistant [on|off|status]";
    public override bool IsHidden => true;
    protected override string ArgumentHintText => "[on|off|status]";

    protected override ToggleAction? ResolveToggleAction(string args)
    {
        var lower = args.ToLowerInvariant();
        return lower switch
        {
            "1" => ToggleAction.On,
            "0" => ToggleAction.Off,
            _ => ToggleActionExtensions.FromValue(args),
        };
    }

    protected override ToggleNullAction NullAction => ToggleNullAction.Status;

    protected override Task OnEnabledAsync(ChatCommandContext context)
    {
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AssistantMode, "1");
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantModeEnabled));
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantEnvVarSet, JccEnvVarConstants.AssistantMode));
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantPersistHint));
        return Task.CompletedTask;
    }

    protected override Task OnDisabledAsync(ChatCommandContext context)
    {
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AssistantMode, "0");
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantModeDisabled));
        return Task.CompletedTask;
    }

    protected override Task PrintStatusAsync(ChatCommandContext context)
    {
        var assistantService = GetService<IAssistantModeService>(context);
        if (assistantService is null) return Task.CompletedTask;

        var enabled = assistantService.IsAssistantModeEnabled;
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantModeStatusHeader));
        TerminalHelper.WriteLine(enabled ? L.T(StringKey.HostAssistantStatusEnabled) : L.T(StringKey.HostAssistantStatusDisabled));
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantEnvStatusLabel, JccEnvVarConstants.AssistantMode, Environment.GetEnvironmentVariable(JccEnvVarConstants.AssistantMode) ?? "(未设置)"));

        return Task.CompletedTask;
    }
}
