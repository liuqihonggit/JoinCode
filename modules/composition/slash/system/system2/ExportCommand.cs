
namespace JoinCode.ChatCommands;

/// <summary>
/// /export 命令 — 导出对话到文件或剪贴板
/// 支持参数：文件名直接写文件，--clipboard 复制到剪贴板，无参数进入交互选择
/// 对齐 TS ExportDialog 实现，自动从首条用户消息提取智能文件名
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Export, Description = "导出对话到文件或剪贴板", Usage = "/export [filename|--clipboard]", Category = ChatCommandCategory.System, ArgumentHint = "[filename|--clipboard]")]
[ChatCommandArg("target", Type = "string", Description = "导出目标：文件名或 --clipboard", Enum = new[] { "--clipboard" })]
public sealed class ExportCommand : ChatCommandBase {
    /// <summary>
    /// 执行 /export 命令 — 根据参数选择导出到剪贴板、指定文件或交互式选择
    /// </summary>
    /// <param name="context">命令执行上下文，包含参数、会话 ID、取消令牌等</param>
    /// <returns>命令执行结果（始终为 Continue，表示不中断主对话流）</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var history = await context.GetCommandServices().ChatService.GetMessageListAsync(context.CancellationToken).ConfigureAwait(false);
        var content = BuildExportContent(history);
        var args = ChatCommandBase.GetNormalizedArgs(context);

        // 对齐 TS: --clipboard 参数 → 复制到剪贴板
        if (args.Equals("--clipboard", StringComparison.OrdinalIgnoreCase)) {
            await CopyToClipboardAsync(context, content).ConfigureAwait(false);
            return ChatCommandResult.Continue();
        }

        // 有文件名参数时直接写文件
        if (!string.IsNullOrEmpty(args) && !args.StartsWith("-")) {
            await WriteToFileAsync(args, content, context.CancellationToken, context.GetCommandServices().FileSystem).ConfigureAwait(false);
            return ChatCommandResult.Continue();
        }

        // 无参数：交互式选择导出方式
        // 对齐 TS: ExportDialog — 选择导出格式和目标
        if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive) {
            var dialog = new Dialog("导出对话", "选择导出方式:", ["保存到文件", "复制到剪贴板", "取消"]);
            var result = await dialog.ShowAsync(context.CancellationToken).ConfigureAwait(false);

            if (result.Cancelled || result.SelectedIndex == 2) {
                TerminalHelper.WriteLine("已取消");
                return ChatCommandResult.Continue();
            }

            if (result.SelectedIndex == 1) {
                await CopyToClipboardAsync(context, content).ConfigureAwait(false);
                return ChatCommandResult.Continue();
            }

            // 保存到文件
            var defaultFilename = ExtractSmartFilename(history);
            await WriteToFileAsync(defaultFilename, content, context.CancellationToken, context.GetCommandServices().FileSystem).ConfigureAwait(false);
            return ChatCommandResult.Continue();
        }

        // 非交互模式回退：自动保存到文件
        var fallbackFilename = ExtractSmartFilename(history);
        await WriteToFileAsync(fallbackFilename, content, context.CancellationToken, context.GetCommandServices().FileSystem).ConfigureAwait(false);
        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 复制内容到剪贴板并输出结果提示
    /// </summary>
    private static async Task CopyToClipboardAsync(ChatCommandContext context, string content) {
        var clipboard = context.GetCommandServices().ClipboardService;
        if (clipboard is not null) {
            await clipboard.SetTextAsync(content, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine($"{TerminalColors.Success}已复制对话到剪贴板{AnsiStyleEnumConstants.Reset}");
        } else {
            TerminalHelper.WriteLine($"{TerminalColors.Error}剪贴板服务不可用{AnsiStyleEnumConstants.Reset}");
        }
    }

    private static async Task WriteToFileAsync(string filename, string content, CancellationToken ct, IFileSystem fs) {
        // 对齐 TS: 强制 .txt 后缀
        if (!filename.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) {
            var dotIndex = filename.LastIndexOf('.');
            filename = dotIndex > 0
                ? filename[..dotIndex] + ".txt"
                : filename + ".txt";
        }

        try {
            var filePath = Path.GetFullPath(filename);
            await fs.WriteAllTextAsync(filePath, content, ct).ConfigureAwait(false);
            TerminalHelper.WriteLine($"{TerminalColors.Success}已导出到: {filePath}{AnsiStyleEnumConstants.Reset}");
        } catch (Exception ex) {
            ChatCommandBase.HandleError("导出", ex);
        }
    }

    private static string BuildExportContent(IReadOnlyList<ApiMessageRecord> history) {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# 对话导出");
        sb.AppendLine($"导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        if (history is null || history.Count == 0) {
            sb.AppendLine("(暂无对话记录)");
            return sb.ToString();
        }

        foreach (var message in history) {
            var role = message.Role.Equals(MessageRoleEnumConstants.User, StringComparison.OrdinalIgnoreCase) ? "👤 用户"
                : message.Role.Equals(MessageRoleEnumConstants.Assistant, StringComparison.OrdinalIgnoreCase) ? "🤖 助手"
                : message.Role.Equals(MessageRoleEnumConstants.System, StringComparison.OrdinalIgnoreCase) ? "⚙️ 系统"
                : message.Role;
            sb.AppendLine($"## {role}");
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string ExtractSmartFilename(IReadOnlyList<ApiMessageRecord> history) {
        // 对齐 TS: 时间戳格式 YYYY-MM-DD-HHmmss
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");

        if (history is null || history.Count == 0)
            return $"conversation-{timestamp}.txt";

        var firstUserMessage = history.FirstOrDefault(m =>
            m.Role.Equals(MessageRoleEnumConstants.User, StringComparison.OrdinalIgnoreCase));

        if (firstUserMessage is null)
            return $"conversation-{timestamp}.txt";

        var content = firstUserMessage.Content.Trim();
        // 对齐 TS: 只取第一行
        var firstLine = content.Split('\n')[0].Trim();

        if (string.IsNullOrEmpty(firstLine))
            return $"conversation-{timestamp}.txt";

        // 对齐 TS: 长度限制在 sanitize 前发生（extractFirstPrompt 截 49 字符 + '…'，
        // 其中 '…' 非 [a-z0-9\s-] 会被 sanitize 剥离 — 等效于直接截断且文件名不含省略号）
        var firstLineLimited = firstLine.Length > 50 ? firstLine[..49] : firstLine;

        // 对齐 TS: sanitizeFilename — 仅保留 [a-z0-9 空白 -]（ASCII），CJK 等非 ASCII 剥离
        var safeName = SanitizeFilename(firstLineLimited);

        if (string.IsNullOrEmpty(safeName))
            return $"conversation-{timestamp}.txt";

        // 对齐 TS: 格式为 {timestamp}-{sanitized}.txt
        return $"{timestamp}-{safeName}.txt";
    }

    /// <summary>
    /// 对齐 TS sanitizeFilename — 转小写后仅保留 [a-z0-9 空白 -]（CJK 等非 ASCII 全剥离），
    /// 空白转 '-'、连续 '-' 收敛为单个、去除首尾 '-'
    /// </summary>
    /// <param name="text">已截断的首条用户消息第一行</param>
    /// <returns>sanitize 后的安全文件名片段；全剥离时为空串</returns>
    private static string SanitizeFilename(string text) =>
        JoinHyphenRuns(KeepAsciiRunes(text));

    /// <summary>
    /// 字符级白名单过滤 — a-z0-9 原样保留（A-Z 转小写），空白与 '-' 归一为 '-'，其余（含 CJK）剥离
    /// </summary>
    private static string KeepAsciiRunes(string text) =>
        string.Concat(text.Select(NormalizeRune).Where(c => c != '\0'));

    /// <summary>
    /// 单字符归一 — 对齐 TS toLowerCase + replace(/[^a-z0-9\s-]/g,'') 的字符级判定；
    /// 返回 '\0' 表示剥离（'\0' 本身即属剥离集合，可安全用作哨兵）
    /// </summary>
    private static char NormalizeRune(char c) => c switch {
        >= 'a' and <= 'z' or >= '0' and <= '9' => c,
        >= 'A' and <= 'Z' => char.ToLowerInvariant(c),
        '-' or ' ' or '\t' or '\n' or '\r' or '\f' or '\v' => '-',
        _ => '\0',
    };

    /// <summary>
    /// 连字符收敛 — 对齐 TS replace(/-+/g,'-') + replace(/^-|-$/g,'')：按 '-' 分段去空后重组
    /// </summary>
    private static string JoinHyphenRuns(string text) =>
        string.Join("-", text.Split('-', StringSplitOptions.RemoveEmptyEntries));
}