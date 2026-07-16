namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Vim, Description = "切换 Vim 输入模式 (无参数时 toggle)", Usage = "/vim [on|off]", Category = ChatCommandCategory.Config)]
public sealed class VimCommand : ToggleCommandBase
{
    public override string Name => ChatCommandNameConstants.Vim;
    public override string Description => "切换 Vim 输入模式 (无参数时 toggle)";
    public override string Usage => "/vim [on|off]";

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

    protected override Task OnDisabledAsync(ChatCommandContext context)
    {
        var vimEngine = GetService<IVimEngine>(context);
        var editorModeService = GetService<IEditorModeService>(context);

        vimEngine?.Disable();
        editorModeService?.SetMode(EditorMode.Normal);
        TerminalHelper.WriteLine("Vim 模式: 已禁用");

        return Task.CompletedTask;
    }

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

    protected override Task PrintStatusAsync(ChatCommandContext context) => Task.CompletedTask;
}
