
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Vim, Description = "切换 Vim 输入模式 (无参数时 toggle)", Usage = "/vim [on|off]", Category = ChatCommandCategory.Config)]
public sealed class VimCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Vim;
    public string Description => "切换 Vim 输入模式 (无参数时 toggle)";
    public string Usage => "/vim [on|off]";
    public string[] Aliases => [];
    public string ArgumentHint => "[on|off]";
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var vimEngine = ChatCommandBase.GetService<IVimEngine>(context);
        var editorModeService = ChatCommandBase.GetService<IEditorModeService>(context);

        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();

        // ToggleAction 枚举标准映射 (on/off/toggle/status),enable/disable 走别名映射
        var toggle = args switch
        {
            "enable" => ToggleAction.On,
            "disable" => ToggleAction.Off,
            _ => ToggleActionExtensions.FromValue(args),
        };

        switch (toggle)
        {
            case ToggleAction.On:
                vimEngine?.Enable();
                editorModeService?.SetMode(EditorMode.Vim);
                TerminalHelper.WriteLine("Vim 模式: 已启用");
                TerminalHelper.WriteLine("使用 hjkl 移动，i 进入插入模式，Esc 返回普通模式");
                break;
            case ToggleAction.Off:
                vimEngine?.Disable();
                editorModeService?.SetMode(EditorMode.Normal);
                TerminalHelper.WriteLine("Vim 模式: 已禁用");
                break;
            default:
            {
                // 无参数 / status / 未知 — 走 toggle 语义
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
                break;
            }
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }
}
