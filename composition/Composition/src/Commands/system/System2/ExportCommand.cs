
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Export, Description = "导出对话到文件或剪贴板", Usage = "/export [filename|--clipboard]", Category = ChatCommandCategory.System, ArgumentHint = "[filename|--clipboard]")]
public sealed class ExportCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var history = await context.Services.ChatService.GetMessageListAsync(context.CancellationToken).ConfigureAwait(false);
        var content = BuildExportContent(history);
        var args = ChatCommandBase.GetNormalizedArgs(context);

        // 对齐 TS: --clipboard 参数 → 复制到剪贴板
        if (args.Equals("--clipboard", StringComparison.OrdinalIgnoreCase))
        {
            var clipboard = context.Services.ClipboardService;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(content, context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.WriteLine($"{TerminalColors.Success}已复制对话到剪贴板{AnsiStyleConstants.Reset}");
            }
            else
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}剪贴板服务不可用{AnsiStyleConstants.Reset}");
            }

            return ChatCommandResult.Continue();
        }

        // 有文件名参数时直接写文件
        if (!string.IsNullOrEmpty(args) && !args.StartsWith("-"))
        {
            await WriteToFileAsync(args, content, context.CancellationToken, context.Services.FileSystem).ConfigureAwait(false);
            return ChatCommandResult.Continue();
        }

        // 无参数：交互式选择导出方式
        // 对齐 TS: ExportDialog — 选择导出格式和目标
        if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            var dialog = new Dialog("导出对话", "选择导出方式:", ["保存到文件", "复制到剪贴板", "取消"]);
            var result = await dialog.ShowAsync(context.CancellationToken).ConfigureAwait(false);

            if (result.Cancelled || result.SelectedIndex == 2)
            {
                TerminalHelper.WriteLine("已取消");
                return ChatCommandResult.Continue();
            }

            if (result.SelectedIndex == 1)
            {
                var clipboard = context.Services.ClipboardService;
                if (clipboard is not null)
                {
                    await clipboard.SetTextAsync(content, context.CancellationToken).ConfigureAwait(false);
                    TerminalHelper.WriteLine($"{TerminalColors.Success}已复制对话到剪贴板{AnsiStyleConstants.Reset}");
                }
                else
                {
                    TerminalHelper.WriteLine($"{TerminalColors.Error}剪贴板服务不可用{AnsiStyleConstants.Reset}");
                }
                return ChatCommandResult.Continue();
            }

            // 保存到文件
            var defaultFilename = ExtractSmartFilename(history);
            await WriteToFileAsync(defaultFilename, content, context.CancellationToken, context.Services.FileSystem).ConfigureAwait(false);
            return ChatCommandResult.Continue();
        }

        // 非交互模式回退：自动保存到文件
        var fallbackFilename = ExtractSmartFilename(history);
        await WriteToFileAsync(fallbackFilename, content, context.CancellationToken, context.Services.FileSystem).ConfigureAwait(false);
        return ChatCommandResult.Continue();
    }

    private static async Task WriteToFileAsync(string filename, string content, CancellationToken ct, IFileSystem fs)
    {
        // 对齐 TS: 强制 .txt 后缀
        if (!filename.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var dotIndex = filename.LastIndexOf('.');
            filename = dotIndex > 0
                ? filename[..dotIndex] + ".txt"
                : filename + ".txt";
        }

        try
        {
            var filePath = Path.GetFullPath(filename);
            await fs.WriteAllTextAsync(filePath, content, ct).ConfigureAwait(false);
            TerminalHelper.WriteLine($"{TerminalColors.Success}已导出到: {filePath}{AnsiStyleConstants.Reset}");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("导出", ex);
        }
    }

    private static string BuildExportContent(IReadOnlyList<ApiMessageRecord> history)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# 对话导出");
        sb.AppendLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        if (history is null || history.Count == 0)
        {
            sb.AppendLine("(暂无对话记录)");
            return sb.ToString();
        }

        foreach (var message in history)
        {
            var role = message.Role.Equals(MessageRoleConstants.User, StringComparison.OrdinalIgnoreCase) ? "👤 用户"
                : message.Role.Equals(MessageRoleConstants.Assistant, StringComparison.OrdinalIgnoreCase) ? "🤖 助手"
                : message.Role.Equals(MessageRoleConstants.System, StringComparison.OrdinalIgnoreCase) ? "⚙️ 系统"
                : message.Role;
            sb.AppendLine($"## {role}");
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string ExtractSmartFilename(IReadOnlyList<ApiMessageRecord> history)
    {
        // 对齐 TS: 时间戳格式 YYYY-MM-DD-HHmmss
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");

        if (history is null || history.Count == 0)
            return $"conversation-{timestamp}.txt";

        var firstUserMessage = history.FirstOrDefault(m =>
            m.Role.Equals(MessageRoleConstants.User, StringComparison.OrdinalIgnoreCase));

        if (firstUserMessage is null)
            return $"conversation-{timestamp}.txt";

        var content = firstUserMessage.Content.Trim();
        // 对齐 TS: 只取第一行
        var firstLine = content.Split('\n')[0].Trim();

        if (string.IsNullOrEmpty(firstLine))
            return $"conversation-{timestamp}.txt";

        // 对齐 TS: sanitizeFilename — 转小写，移除非 [a-z0-9\s-]，空格替换为-，合并连续-
        var safeName = new string(firstLine
            .Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-'
                ? char.IsUpper(c) ? char.ToLowerInvariant(c) : c
                : '\0')
            .ToArray())
            .Trim('\0')
            .Replace(' ', '-')
            .Replace("---", "-").Replace("--", "-")
            .Trim('-');

        // 对齐 TS: 长度限制 50 字符
        if (safeName.Length > 50)
            safeName = safeName[..49] + "…";

        if (string.IsNullOrEmpty(safeName))
            return $"conversation-{timestamp}.txt";

        // 对齐 TS: 格式为 {timestamp}-{sanitized}.txt
        return $"{timestamp}-{safeName}.txt";
    }
}
