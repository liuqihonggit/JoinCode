namespace JoinCode.ChatCommands;

/// <summary>
/// /rename 命令 — 重命名当前会话，通过 TranscriptService 追加 custom-title 元数据。
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Rename, Description = "重命名当前会话", Usage = "/rename <new-name>", Category = ChatCommandCategory.Session, ArgumentHint = "<new-name>")]
[ChatCommandArg("new_name", Type = "string", Description = "新的会话名称", Required = true)]
public sealed class RenameCommand : ChatCommandBase
{
    /// <summary>
    /// 执行 /rename 命令，为当前会话设置新的自定义标题。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>命令执行结果。</returns>
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var newName = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(newName))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}用法: /rename <new-name>{AnsiStyleEnumConstants.Reset}");
            TerminalHelper.WriteLine("为当前会话设置一个新的名称");
            return ChatCommandResult.Continue();
        }

        try
        {
            // 对齐 TS: /rename 追加 custom-title 元数据条目，不修改 sessionId 或文件名
            var transcriptService = ChatCommandBase.GetService<ITranscriptService>(context, typeof(ITranscriptService));
            if (transcriptService is not null)
            {
                var oldTitle = await transcriptService.GetCustomTitleAsync(context.SessionId, context.CancellationToken).ConfigureAwait(false);
                await transcriptService.SaveCustomTitleAsync(context.SessionId, newName, context.CancellationToken).ConfigureAwait(false);

                var fromTitle = string.IsNullOrEmpty(oldTitle) ? context.SessionId : oldTitle;
                TerminalHelper.WriteLine($"{TerminalColors.Success}会话已重命名: {fromTitle} → {newName}{AnsiStyleEnumConstants.Reset}");
            }
            else
            {
                // 回退: 无 TranscriptService 时使用直接文件操作（旧模式）
                await RenameViaFileAsync(context, newName).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("重命名", ex);
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 回退: 直接文件操作（旧模式，仅在无 TranscriptService 时使用）
    /// </summary>
    private static async Task RenameViaFileAsync(ChatCommandContext context, string newName)
    {
        var sessionsDir = Path.Combine(
            AppDataConstants.Paths.JccDirectory,
            "sessions");

        var sessionFile = Path.Combine(sessionsDir, $"{context.SessionId}.json");
        var fs = context.GetCommandServices().FileSystem;

        if (!fs.FileExists(sessionFile))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}会话文件不存在: {context.SessionId}{AnsiStyleEnumConstants.Reset}");
            return;
        }

        var json = await fs.ReadAllTextAsync(sessionFile, context.CancellationToken).ConfigureAwait(false);
        var session = RelaxedJsonSerializer.Deserialize(json, CliJsonContext.Default.SessionData);

        var oldName = session?.Id ?? context.SessionId;

        var newSessionFile = Path.Combine(sessionsDir, $"{newName}.json");

        if (fs.FileExists(newSessionFile) && !string.Equals(newName, context.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            TerminalHelper.WriteLine($"{TerminalColors.Warning}会话 '{newName}' 已存在{AnsiStyleEnumConstants.Reset}");
            return;
        }

        if (session is not null)
        {
            session.Id = newName;
            var updatedJson = JsonSerializer.Serialize(session, CliIndentedJsonContext.Default.SessionData);
            await fs.WriteAllTextAsync(sessionFile, updatedJson, context.CancellationToken).ConfigureAwait(false);
        }

        if (!string.Equals(context.SessionId, newName, StringComparison.OrdinalIgnoreCase))
        {
            fs.MoveFile(sessionFile, newSessionFile, overwrite: false);
        }

        TerminalHelper.WriteLine($"{TerminalColors.Success}会话已重命名: {oldName} → {newName}{AnsiStyleEnumConstants.Reset}");
    }
}
