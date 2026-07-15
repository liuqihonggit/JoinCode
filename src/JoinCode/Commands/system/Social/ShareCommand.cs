namespace JoinCode.ChatCommands;

/// <summary>
/// /share 命令 — 对齐 TS share/
/// TS 使用 ClipboardService + 文件导出分享对话/代码片段
/// 对齐内容：对话导出为 Markdown + 保存到文件
/// 架构差异：TS 有 React 分享面板+URL分享，C# 为文件导出
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Share, Description = "生成可分享的对话内容", Usage = "/share", Category = ChatCommandCategory.Social)]
public sealed class ShareCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Share;
    public string Description => "生成可分享的对话内容";
    public string Usage => "/share";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        TerminalHelper.WriteLine($"{TerminalColors.Primary}生成分享内容...{AnsiStyleConstants.Reset}");
        TerminalHelper.NewLine();

        try
        {
            var history = await context.Services.ChatService.GetMessageListAsync(context.CancellationToken);

            if (history.Count == 0)
            {
                TerminalHelper.WriteLine($"  {TerminalColors.Muted}暂无对话内容可分享{AnsiStyleConstants.Reset}");
                return ChatCommandResult.Continue();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# JoinCode 对话分享");
            sb.AppendLine($"> 会话ID: {context.SessionId}");
            sb.AppendLine($"> 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"> 消息数: {history.Count}");
            sb.AppendLine();

            foreach (var msg in history)
            {
                var roleLabel = string.Equals(msg.Role, MessageRoleConstants.User, StringComparison.OrdinalIgnoreCase)
                    ? "👤 User" : "🤖 Assistant";

                sb.AppendLine($"## {roleLabel}");
                sb.AppendLine(msg.Content);
                sb.AppendLine();
            }

            var content = sb.ToString();

            TerminalHelper.WriteLine($"{TerminalColors.Success}分享内容已生成{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"  长度: {content.Length:N0} 字符");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("--- 预览（前500字符）---");
            TerminalHelper.WriteLine(content.Length > 500 ? content[..500] + "..." : content);
            TerminalHelper.WriteLine("--- 结束 ---");

            try
            {
                var sharePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    AppDataConstants.AppDataFolder, "shares",
                    $"share-{DateTime.Now:yyyyMMdd-HHmmss}.md");

                var dir = Path.GetDirectoryName(sharePath);
                var fs = context.Services.FileSystem;
                if (!string.IsNullOrEmpty(dir) && !fs.DirectoryExists(dir))
                    DirectoryHelper.EnsureDirectoryExists(fs, dir);

                await fs.WriteAllTextAsync(sharePath, content, context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.NewLine();
                TerminalHelper.WriteLine($"{TerminalColors.Success}已保存到: {sharePath}{AnsiStyleConstants.Reset}");
            }
            catch (Exception ex)
            {
                TerminalHelper.NewLine();
                ChatCommandBase.HandleError("保存分享文件", ex);
            }
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("生成分享内容", ex);
        }

        return ChatCommandResult.Continue();
    }
}