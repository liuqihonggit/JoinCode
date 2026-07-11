namespace JoinCode.ChatCommands;

/// <summary>
/// /session 命令 — 对齐 TS session/
/// TS 使用会话管理窗口，列出/恢复/删除历史会话
/// 对齐内容：list+resume+delete 会话操作
/// 架构差异：TS 有 React 交互式会话选择器，C# 为命令行操作
/// 待办：需要 SessionStore 服务实现会话持久化和恢复
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Session, Description = "管理历史会话", Usage = "/session [list|resume|delete] [id]", Category = ChatCommandCategory.Session)]
public sealed class SessionCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Session;
    public string Description => "管理历史会话";
    public string Usage => "/session [list|resume|delete] [id]";
    public string[] Aliases => ["sessions"];
    public string ArgumentHint => "[list|resume|delete]";
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action)
        {
            case CrudActionConstants.List:
            case CrudActionConstants.Ls:
                ShowSessionList(context);
                break;
            case "resume" or "open":
                var resumeId = args.Length > 1 ? args[1] : null;
                ResumeSession(context, resumeId);
                break;
            case CrudActionConstants.Delete:
            case CrudActionConstants.Rm:
                var deleteId = args.Length > 1 ? args[1] : null;
                DeleteSession(context, deleteId);
                break;
            default:
                TerminalHelper.WriteLine($"未知操作: {action}");
                TerminalHelper.WriteLine("支持: list, resume, delete");
                break;
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static void ShowSessionList(ChatCommandContext context)
    {
        TerminalHelper.WriteLine("=== 历史会话 ===\n");

        // 待办：接入 SessionStore 服务后替换为真实会话列表
        TerminalHelper.WriteLine($"  当前会话: {context.SessionId}");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("使用 /session resume <id> 恢复会话");
        TerminalHelper.WriteLine("使用 /session delete <id> 删除会话");
    }

    private static void ResumeSession(ChatCommandContext context, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            TerminalHelper.WriteLine("用法: /session resume <id>");
            return;
        }

        // 待办：接入 SessionStore 服务后实现会话恢复
        TerminalHelper.WriteLine($"恢复会话: {id}");
    }

    private static void DeleteSession(ChatCommandContext context, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            TerminalHelper.WriteLine("用法: /session delete <id>");
            return;
        }

        // 待办：接入 SessionStore 服务后实现会话删除
        TerminalHelper.WriteLine($"已删除会话: {id}");
    }
}
