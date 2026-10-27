namespace JoinCode.ChatCommands;

/// <summary>
/// /branch 命令 — 对齐 TS branch/
/// TS 使用对话分支功能，创建/切换/列表对话分支
/// 对齐内容：list+create+switch+delete 分支操作
/// 架构差异：TS 有 React 交互式分支树，C# 为命令行操作
/// 注意：分支管理服务(BranchManager)尚未实现，当前所有操作诚实拒绝而非假装成功
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Branch, Description = "管理对话分支", Usage = "/branch [list|create|switch|delete] [name]", Category = ChatCommandCategory.Session, Aliases = ["branches"], ArgumentHint = "[list|create|switch|delete]")]
[ChatCommandArg("action", Type = "string", Description = "分支操作", Enum = new[] { "list", "create", "switch", "delete" })]
[ChatCommandArg("name", Type = "string", Description = "分支名称")]
public sealed class BranchCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /branch 命令，按子操作分发到对应分支处理流程。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>表示异步操作的任务，承载命令执行结果。</returns>
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action) {
            case CrudActionEnumConstants.List:
            case CrudActionEnumConstants.Ls:
            ShowBranchList(context);
            break;
            case CrudActionEnumConstants.Create:
            case CrudActionEnumConstants.New:
            var createName = args.Length > 1 ? args[1] : null;
            CreateBranch(context, createName);
            break;
            case "switch" or "go":
            var switchName = args.Length > 1 ? args[1] : null;
            SwitchBranch(context, switchName);
            break;
            case CrudActionEnumConstants.Delete:
            case CrudActionEnumConstants.Rm:
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

    private static void ShowBranchList(ChatCommandContext context) {
        TerminalHelper.WriteLine("=== 对话分支 ===\n");
        TerminalHelper.WriteLine("  ⚠ 分支管理服务尚未实现，无法列出分支。");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("如需使用对话分支功能，请等待 BranchManager 服务落地。");
    }

    private static void CreateBranch(ChatCommandContext context, string? name) {
        if (string.IsNullOrEmpty(name)) {
            TerminalHelper.WriteLine("用法: /branch create <name>");
            return;
        }

        TerminalHelper.WriteLine($"  ⚠ 分支管理服务尚未实现，无法创建分支 '{name}'。");
    }

    private static void SwitchBranch(ChatCommandContext context, string? name) {
        if (string.IsNullOrEmpty(name)) {
            TerminalHelper.WriteLine("用法: /branch switch <name>");
            return;
        }

        TerminalHelper.WriteLine($"  ⚠ 分支管理服务尚未实现，无法切换到分支 '{name}'。");
    }

    private static void DeleteBranch(ChatCommandContext context, string? name) {
        if (string.IsNullOrEmpty(name)) {
            TerminalHelper.WriteLine("用法: /branch delete <name>");
            return;
        }

        TerminalHelper.WriteLine($"  ⚠ 分支管理服务尚未实现，无法删除分支 '{name}'。");
    }
}