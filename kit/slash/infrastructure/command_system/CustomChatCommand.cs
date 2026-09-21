namespace JoinCode.ChatCommands;

/// <summary>
/// 自定义聊天命令 — 包装用户配置的 CustomCommand，支持参数插值后发送给模型或直接输出
/// </summary>
public sealed class CustomChatCommand : IChatCommand {
    private readonly CustomCommand _command;

    /// <summary>命令名称 — 取自 CustomCommand.FullName</summary>
    public string Name => _command.FullName;
    /// <summary>命令描述 — 为空时回退到"自定义命令: 全名"</summary>
    public string Description => string.IsNullOrEmpty(_command.Description) ? $"自定义命令: {_command.FullName}" : _command.Description;
    /// <summary>命令用法 — /全名 参数占位</summary>
    public string Usage => $"/{_command.FullName} <参数>";
    /// <summary>命令别名 — 自定义命令无别名</summary>
    public string[] Aliases => [];
    /// <summary>参数提示文本 — 自定义命令无固定提示</summary>
    public string ArgumentHint => string.Empty;
    /// <summary>是否隐藏命令 — 自定义命令默认不隐藏</summary>
    public bool IsHidden => false;

    /// <summary>
    /// 原始 CustomCommand 配置对象
    /// </summary>
    public CustomCommand Command => _command;

    /// <summary>
    /// 构造自定义聊天命令实例
    /// </summary>
    /// <param name="command">自定义命令配置</param>
    public CustomChatCommand(CustomCommand command) {
        _command = command;
    }

    /// <summary>
    /// 执行命令 — 对参数插值生成 prompt，根据 DisableModelInvocation 决定直接输出或发送给模型
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>命令执行结果</returns>
    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var arguments = ChatCommandBase.GetNormalizedArgs(context);
        var prompt = _command.ApplyArguments(arguments);

        if (_command.DisableModelInvocation) {
            TerminalHelper.WriteLine(prompt);
            return ChatCommandResult.Continue();
        }

        try {
            var result = await context.GetCommandServices().ChatService.SendMessageAsync(prompt).ConfigureAwait(false);
            TerminalHelper.WriteLine(result);
        } catch (Exception ex) {
            ChatCommandBase.HandleError("自定义命令执行", ex);
        }

        return ChatCommandResult.Continue();
    }
}