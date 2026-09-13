namespace JoinCode.ChatCommands;

/// <summary>
/// /vim 命令 — 切换 Vim 输入模式
/// 支持显式 on/off 以及无参数时的 toggle 切换
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Vim, Description = "切换 Vim 输入模式 (无参数时 toggle)", Usage = "/vim [on|off]", Category = ChatCommandCategory.Config)]
[ChatCommandArg("action", Type = "string", Description = "Vim 模式动作,省略时 toggle", Enum = new[] { "on", "off" })]
public sealed class VimCommand : ToggleCommandBase
{
    /// <summary>
    /// 获取命令名称
    /// </summary>
    public override string Name => ChatCommandNameConstants.Vim;

    /// <summary>
    /// 获取命令描述
    /// </summary>
    public override string Description => "切换 Vim 输入模式 (无参数时 toggle)";

    /// <summary>
    /// 获取命令用法
    /// </summary>
    public override string Usage => "/vim [on|off]";

    /// <summary>
    /// 从参数解析切换动作 — 支持 enable/disable 别名及 on/off 标准值
    /// </summary>
    /// <param name="args">原始参数字符串</param>
    /// <returns>解析得到的切换动作,无法解析时返回 null</returns>
    protected override ToggleAction? ResolveToggleAction(string args)
    {
        var lower = args.ToLowerInvariant();
        return lower switch
        {
            "enable" => ToggleAction.On,
            "disable" => ToggleAction.Off,
            _ => ToggleActionExtensions.FromValue(args),
        };
    }

    /// <summary>
    /// 启用 Vim 模式 — 激活 Vim 引擎并切换编辑模式,输出按键提示
    /// </summary>
    /// <param name="context">命令执行上下文,提供参数、服务、取消令牌等</param>
    /// <returns>表示异步操作完成的任务</returns>
    protected override Task OnEnabledAsync(ChatCommandContext context)
    {
        var vimEngine = GetService<IVimEngine>(context);
        var editorModeService = GetService<IEditorModeService>(context);

        vimEngine?.Enable();
        editorModeService?.SetMode(EditorMode.Vim);
        TerminalHelper.WriteLine("Vim 模式: 已启用");
        TerminalHelper.WriteLine("使用 hjkl 移动，i 进入插入模式，Esc 返回普通模式");

        return Task.CompletedTask;
    }

    /// <summary>
    /// 禁用 Vim 模式 — 停用 Vim 引擎并恢复标准编辑模式
    /// </summary>
    /// <param name="context">命令执行上下文,提供参数、服务、取消令牌等</param>
    /// <returns>表示异步操作完成的任务</returns>
    protected override Task OnDisabledAsync(ChatCommandContext context)
    {
        var vimEngine = GetService<IVimEngine>(context);
        var editorModeService = GetService<IEditorModeService>(context);

        vimEngine?.Disable();
        editorModeService?.SetMode(EditorMode.Normal);
        TerminalHelper.WriteLine("Vim 模式: 已禁用");

        return Task.CompletedTask;
    }

    /// <summary>
    /// 切换 Vim 模式 — 根据当前编辑模式在 Vim 与 Normal 之间互斥切换
    /// </summary>
    /// <param name="context">命令执行上下文,提供参数、服务、取消令牌等</param>
    /// <returns>表示异步操作完成的任务</returns>
    protected override Task OnToggleAsync(ChatCommandContext context)
    {
        var vimEngine = GetService<IVimEngine>(context);
        var editorModeService = GetService<IEditorModeService>(context);

        var currentMode = editorModeService?.CurrentMode ?? EditorMode.Normal;
        var newMode = currentMode == EditorMode.Vim ? EditorMode.Normal : EditorMode.Vim;

        if (newMode == EditorMode.Vim)
        {
            vimEngine?.Enable();
            editorModeService?.SetMode(EditorMode.Vim);
            TerminalHelper.WriteLine("编辑模式: Vim");
            TerminalHelper.WriteLine("使用 hjkl 移动，i 进入插入模式，Esc 返回普通模式");
        }
        else
        {
            vimEngine?.Disable();
            editorModeService?.SetMode(EditorMode.Normal);
            TerminalHelper.WriteLine("编辑模式: Normal (标准 readline 键绑定)");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 打印当前状态 — Vim 命令不输出额外状态信息,直接返回已完成任务
    /// </summary>
    /// <param name="context">命令执行上下文,提供参数、服务、取消令牌等</param>
    /// <returns>表示异步操作完成的任务</returns>
    protected override Task PrintStatusAsync(ChatCommandContext context) => Task.CompletedTask;
}
