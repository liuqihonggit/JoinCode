namespace JoinCode.ChatCommands;

/// <summary>
/// /session 命令 — 对齐 TS session/
/// TS 使用会话管理窗口，列出/恢复/删除历史会话
/// 对齐内容：list+resume+delete 会话操作
/// 架构差异：TS 有 React 交互式会话选择器，C# 为命令行操作
/// 待办：需要 SessionStore 服务实现会话持久化和恢复
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Session, Description = "管理历史会话", Usage = "/session [list|resume|delete] [id]", Category = ChatCommandCategory.Session, Aliases = ["sessions"], ArgumentHint = "[list|resume|delete]")]
[ChatCommandArg("action", Type = "string", Description = "会话操作", Enum = new[] { "list", "resume", "delete" })]
[ChatCommandArg("id", Type = "string", Description = "会话 ID（resume/delete 时需要）")]
public sealed class SessionCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /session 命令，根据子操作分发到 list/resume/delete 会话管理逻辑。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>命令执行结果。</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var args = ChatCommandBase.GetSplitArgs(context);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action) {
            case CrudActionEnumConstants.List:
            case CrudActionEnumConstants.Ls:
            await ShowSessionList(context).ConfigureAwait(false);
            break;
            case "resume" or "open":
            var resumeId = args.Length > 1 ? args[1] : null;
            await ResumeSession(context, resumeId).ConfigureAwait(false);
            break;
            case CrudActionEnumConstants.Delete:
            case CrudActionEnumConstants.Rm:
            var deleteId = args.Length > 1 ? args[1] : null;
            await DeleteSession(context, deleteId).ConfigureAwait(false);
            break;
            default:
            TerminalHelper.WriteLine($"未知操作: {action}");
            TerminalHelper.WriteLine("支持: list, resume, delete");
            break;
        }

        return ChatCommandResult.Continue();
    }

    private static async Task ShowSessionList(ChatCommandContext context) {
        await ResumeCommand.ListSessionsAsync(context, showAllProjects: false).ConfigureAwait(false);
    }

    private static async Task ResumeSession(ChatCommandContext context, string? id) {
        if (string.IsNullOrEmpty(id)) {
            TerminalHelper.WriteLine("用法: /session resume <id>");
            return;
        }

        await ResumeCommand.ResumeSessionAsync(id, context, ResumeEntrypoint.SlashCommandSessionId).ConfigureAwait(false);
    }

    private static async Task DeleteSession(ChatCommandContext context, string? id) {
        if (string.IsNullOrEmpty(id)) {
            TerminalHelper.WriteLine("用法: /session delete <id>");
            return;
        }

        var transcriptService = ChatCommandBase.GetService<ITranscriptService>(context, typeof(ITranscriptService));
        if (transcriptService is null) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}ITranscriptService 不可用，无法删除会话{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var deleted = await transcriptService.DeleteTranscriptAsync(id, context.CancellationToken).ConfigureAwait(false);
        if (deleted) {
            TerminalHelper.WriteLine($"{TerminalColors.Success}已删除会话: {id}{AnsiStyleEnumConstants.Reset}");
        } else {
            TerminalHelper.WriteLine($"{TerminalColors.Error}会话不存在或删除失败: {id}{AnsiStyleEnumConstants.Reset}");
        }
    }
}