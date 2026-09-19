namespace JoinCode.ChatCommands;

/// <summary>
/// /assistant 命令 — 长期助手模式开关，支持开启、关闭和查看状态
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Assistant, Description = "长期助手模式", Usage = "/assistant [on|off|status]", Category = ChatCommandCategory.Agent)]
[ChatCommandArg("action", Type = "string", Description = "助手模式动作", Enum = new[] { "on", "off", "status" }, Default = "status")]
public sealed class AssistantCommand : ToggleCommandBase {
    /// <summary>
    /// 获取命令名称
    /// </summary>
    public override string Name => ChatCommandNameEnumConstants.Assistant;
    /// <summary>
    /// 获取命令描述
    /// </summary>
    public override string Description => "长期助手模式";
    /// <summary>
    /// 获取命令用法
    /// </summary>
    public override string Usage => "/assistant [on|off|status]";
    /// <summary>
    /// 获取命令是否隐藏
    /// </summary>
    public override bool IsHidden => true;
    /// <summary>
    /// 获取参数提示文本
    /// </summary>
    protected override string ArgumentHintText => "[on|off|status]";

    /// <summary>
    /// 解析切换动作，支持 1/0/on/off/status 等参数
    /// </summary>
    /// <param name="args">用户输入的参数</param>
    /// <returns>切换动作，无法识别时返回 null</returns>
    protected override ToggleAction? ResolveToggleAction(string args) {
        var lower = args.ToLowerInvariant();
        return lower switch {
            "1" => ToggleAction.On,
            "0" => ToggleAction.Off,
            _ => ToggleActionExtensions.FromValue(args),
        };
    }

    /// <summary>
    /// 获取无参数时的默认动作
    /// </summary>
    protected override ToggleNullAction NullAction => ToggleNullAction.Status;

    /// <summary>
    /// 启用长期助手模式时执行，设置环境变量并提示持久化方式
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>完成的任务</returns>
    protected override Task OnEnabledAsync(ChatCommandContext context) {
        Environment.SetEnvironmentVariable(JccEnvVarEnumConstants.AssistantMode, "1");
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantModeEnabled));
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantEnvVarSet, JccEnvVarEnumConstants.AssistantMode));
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantPersistHint));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 禁用长期助手模式时执行，清除环境变量
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>完成的任务</returns>
    protected override Task OnDisabledAsync(ChatCommandContext context) {
        Environment.SetEnvironmentVariable(JccEnvVarEnumConstants.AssistantMode, "0");
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantModeDisabled));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 打印当前助手模式状态，包括开关状态和环境变量取值
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>完成的任务</returns>
    protected override Task PrintStatusAsync(ChatCommandContext context) {
        var assistantService = GetService<IAssistantModeService>(context);
        if (assistantService is null) return Task.CompletedTask;

        var enabled = assistantService.IsAssistantModeEnabled;
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantModeStatusHeader));
        TerminalHelper.WriteLine(enabled ? L.T(StringKey.HostAssistantStatusEnabled) : L.T(StringKey.HostAssistantStatusDisabled));
        TerminalHelper.WriteLine(L.T(StringKey.HostAssistantEnvStatusLabel, JccEnvVarEnumConstants.AssistantMode, Environment.GetEnvironmentVariable(JccEnvVarEnumConstants.AssistantMode) ?? "(未设置)"));

        return Task.CompletedTask;
    }
}