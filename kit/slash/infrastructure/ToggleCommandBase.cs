namespace JoinCode.ChatCommands;

/// <summary>
/// 开关命令抽象基类 — 提供 on/off/无参数 三分支执行骨架，子类只需实现 OnEnabledAsync/OnDisabledAsync
/// </summary>
public abstract class ToggleCommandBase : ChatCommandBase
{
    /// <summary>
    /// 参数提示文本 — 默认 [on|off]
    /// </summary>
    protected virtual string ArgumentHintText => "[on|off]";

    /// <summary>参数提示 — 暴露 ArgumentHintText 给基类</summary>
    public override string ArgumentHint => ArgumentHintText;

    /// <summary>
    /// 解析参数为开关动作 — 默认通过 ToggleActionExtensions.FromValue 解析
    /// </summary>
    /// <param name="args">原始参数字符串</param>
    /// <returns>解析得到的开关动作，无法解析时为 null</returns>
    protected virtual ToggleAction? ResolveToggleAction(string args)
        => ToggleActionExtensions.FromValue(args);

    /// <summary>
    /// 无参数时的行为 — 默认 Toggle（切换当前状态）
    /// </summary>
    protected virtual ToggleNullAction NullAction => ToggleNullAction.Toggle;

    /// <summary>
    /// 执行命令 — 根据 ResolveToggleAction 结果分发到 On/Off/Default 三条分支，最后打印状态
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>命令执行结果</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = GetNormalizedArgs(context);
        var action = ResolveToggleAction(args);

        switch (action)
        {
            case ToggleAction.On:
                await OnEnabledAsync(context).ConfigureAwait(false);
                break;
            case ToggleAction.Off:
                await OnDisabledAsync(context).ConfigureAwait(false);
                break;
            default:
                await OnDefaultAsync(context, args).ConfigureAwait(false);
                break;
        }

        await PrintStatusAsync(context).ConfigureAwait(false);

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 启用命令 — 子类实现具体启用逻辑
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    protected abstract Task OnEnabledAsync(ChatCommandContext context);

    /// <summary>
    /// 禁用命令 — 子类实现具体禁用逻辑
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    protected abstract Task OnDisabledAsync(ChatCommandContext context);

    /// <summary>
    /// 默认分支 — 当参数无法识别为 on/off 时调用，根据 NullAction 决定是否触发切换
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <param name="args">原始参数字符串</param>
    protected virtual Task OnDefaultAsync(ChatCommandContext context, string args)
    {
        if (NullAction == ToggleNullAction.Toggle)
        {
            return OnToggleAsync(context);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 切换开关状态 — 默认无操作，子类可 override 实现切换逻辑
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    protected virtual Task OnToggleAsync(ChatCommandContext context) => Task.CompletedTask;

    /// <summary>
    /// 打印当前状态 — 默认无操作，子类可 override 输出状态信息
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    protected virtual Task PrintStatusAsync(ChatCommandContext context) => Task.CompletedTask;
}

/// <summary>
/// 无参数时的行为策略
/// </summary>
public enum ToggleNullAction
{
    /// <summary>切换当前状态</summary>
    [EnumValue("toggle")]
    Toggle,
    /// <summary>仅显示状态</summary>
    [EnumValue("status")]
    Status,
}
