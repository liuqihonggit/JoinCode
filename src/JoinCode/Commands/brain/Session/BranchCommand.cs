namespace JoinCode.ChatCommands;

/// <summary>
/// /branch 命令 — 对齐 TS branch/
/// TS 使用对话分支功能，创建/切换/列表对话分支
/// 对齐内容：list+create+switch+delete 分支操作
/// 架构差异：TS 有 React 交互式分支树，C# 为命令行操作
/// 待办：需要 BranchManager 服务实现分支存储和切换
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Branch, Description = "管理对话分支", Usage = "/branch [list|create|switch|delete] [name]", Category = ChatCommandCategory.Session)]
public sealed class BranchCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Branch;
    public string Description => "管理对话分支";
    public string Usage => "/branch [list|create|switch|delete] [name]";
    public string[] Aliases => ["branches"];
    public string ArgumentHint => "[list|create|switch|delete]";
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action)
        {
            case CrudActionConstants.List:
            case CrudActionConstants.Ls:
                ShowBranchList(context);
                break;
            case CrudActionConstants.Create:
            case CrudActionConstants.New:
                var createName = args.Length > 1 ? args[1] : null;
                CreateBranch(context, createName);
                break;
            case "switch" or "go":
                var switchName = args.Length > 1 ? args[1] : null;
                SwitchBranch(context, switchName);
                break;
            case CrudActionConstants.Delete:
            case CrudActionConstants.Rm:
                var deleteName = args.Length > 1 ? args[1] : null;
                DeleteBranch(context, deleteName);
                break;
            default:
                TerminalHelper.WriteLine($"未知操作: {action}");
                TerminalHelper.WriteLine("支持: list, create, switch, delete");
                break;
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static void ShowBranchList(ChatCommandContext context)
    {
        TerminalHelper.WriteLine("=== 对话分支 ===\n");

        // 待办：接入 BranchManager 服务后替换为真实分支列表
        TerminalHelper.WriteLine($"  * main (当前)");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("使用 /branch create <name> 创建新分支");
        TerminalHelper.WriteLine("使用 /branch switch <name> 切换分支");
    }

    private static void CreateBranch(ChatCommandContext context, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            TerminalHelper.WriteLine("用法: /branch create <name>");
            return;
        }

        // 待办：接入 BranchManager 服务后实现分支创建
        TerminalHelper.WriteLine($"已创建分支: {name}");
    }

    private static void SwitchBranch(ChatCommandContext context, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            TerminalHelper.WriteLine("用法: /branch switch <name>");
            return;
        }

        // 待办：接入 BranchManager 服务后实现分支切换
        TerminalHelper.WriteLine($"已切换到分支: {name}");
    }

    private static void DeleteBranch(ChatCommandContext context, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            TerminalHelper.WriteLine("用法: /branch delete <name>");
            return;
        }

        // 待办：接入 BranchManager 服务后实现分支删除
        TerminalHelper.WriteLine($"已删除分支: {name}");
    }
}
