
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Copy, Description = "复制最近的 AI 回复到剪贴板（/copy N 复制第N条）", Usage = "/copy [N|code]", Category = ChatCommandCategory.System)]
public sealed class CopyCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Copy;
    public string Description => "复制最近的 AI 回复到剪贴板（/copy N 复制第N条）";
    public string Usage => "/copy [N|code]";
    public string[] Aliases => [];
    public string ArgumentHint => "[N|code]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var clipboardService = context.Services.ClipboardService;
        if (clipboardService is null)
        {
            if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                TerminalHelper.WriteLine("剪贴板服务未初始化");
            }
            return ChatCommandResult.Continue();
        }

        var args = ChatCommandBase.GetNormalizedArgs(context);

        try
        {
            var history = await context.Services.ChatService.GetMessageListAsync(context.CancellationToken).ConfigureAwait(false);
            // 对齐 TS: 只收集有文本内容的助手消息（跳过纯工具调用轮次）
            var assistantMessages = history.Where(m =>
                m.Role.Equals(MessageRoleConstants.Assistant, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(m.Content)).ToList();

            if (assistantMessages.Count == 0)
            {
                TerminalHelper.WriteLine("没有可复制的 AI 消息");
                return ChatCommandResult.Continue();
            }

            if (args.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                args.Equals("c", StringComparison.OrdinalIgnoreCase))
            {
                await CopyCodeBlockAsync(context, assistantMessages).ConfigureAwait(false);
            }
            else if (!string.IsNullOrEmpty(args) && int.TryParse(args, out var n))
            {
                // 对齐 TS: /copy N — N从1开始，1=最新，2=次新...
                if (n < 1 || n > assistantMessages.Count)
                {
                    TerminalHelper.WriteLine($"只有 {assistantMessages.Count} 条助手消息可复制（/copy 1 = 最新）");
                    return ChatCommandResult.Continue();
                }

                var message = assistantMessages[^n];
                await CopyWithFallbackAsync(clipboardService, message.Content, "response.md", context.CancellationToken, context.Services.FileSystem).ConfigureAwait(false);
            }
            else
            {
                // 默认复制最新助手消息
                var lastMessage = assistantMessages[^1];
                await CopyWithFallbackAsync(clipboardService, lastMessage.Content, "response.md", context.CancellationToken, context.Services.FileSystem).ConfigureAwait(false);
            }
        }
        catch (PlatformNotSupportedException)
        {
            TerminalHelper.WriteLine("剪贴板功能在当前平台暂不可用");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("复制", ex);
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 对齐 TS: 复制到剪贴板 + 写入临时文件作为回退
    /// </summary>
    private static async Task CopyWithFallbackAsync(
        IClipboardService clipboardService, string text, string filename, CancellationToken ct, IFileSystem fs)
    {
        await clipboardService.SetTextAsync(text, ct).ConfigureAwait(false);

        var lineCount = text.Count(c => c == '\n') + 1;
        var charCount = text.Length;

        // 对齐 TS: 同时写入临时文件作为回退（OSC52需要终端支持）
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "jcc");
            DirectoryHelper.EnsureDirectoryExists(fs, tempDir);
            var filePath = Path.Combine(tempDir, filename);
            await fs.WriteAllTextAsync(filePath, text, ct).ConfigureAwait(false);
            TerminalHelper.WriteLine($"已复制到剪贴板 ({charCount} 字符, {lineCount} 行)");
            TerminalHelper.WriteLine($"同时写入: {filePath}");
        }
        catch
        {
            TerminalHelper.WriteLine($"已复制到剪贴板 ({charCount} 字符, {lineCount} 行)");
        }
    }

    private static async Task CopyCodeBlockAsync(ChatCommandContext context, List<ApiMessageRecord> assistantMessages)
    {
        var clipboardService = context.Services.ClipboardService!;

        foreach (var message in assistantMessages.AsEnumerable().Reverse())
        {
            var content = message.Content;
            var codeStart = content.IndexOf("```", StringComparison.Ordinal);
            if (codeStart < 0) continue;

            var afterLang = content.IndexOf('\n', codeStart);
            if (afterLang < 0) continue;

            var codeEnd = content.IndexOf("```", afterLang + 1, StringComparison.Ordinal);
            if (codeEnd < 0) continue;

            var code = content.AsSpan(afterLang + 1, codeEnd - afterLang - 1).Trim().ToString();

            // 对齐 TS: 提取语言标识作为文件扩展名
            var langSpan = content.AsSpan(codeStart + 3, afterLang - codeStart - 3).Trim();
            var ext = GetFileExtension(langSpan);
            var filename = $"copy{ext}";

            await CopyWithFallbackAsync(clipboardService, code, filename, context.CancellationToken, context.Services.FileSystem).ConfigureAwait(false);
            return;
        }

        TerminalHelper.WriteLine("没有找到可复制的代码块");
    }

    /// <summary>
    /// 对齐 TS: fileExtension — 将语言标识映射为文件扩展名
    /// </summary>
    private static string GetFileExtension(ReadOnlySpan<char> lang)
    {
        if (lang.IsEmpty) return ".txt";

        // 清理非字母数字字符（防止路径遍历）
        var sanitized = new char[lang.Length];
        var len = 0;
        foreach (var c in lang)
        {
            if (char.IsLetterOrDigit(c))
                sanitized[len++] = c;
        }

        if (len == 0) return ".txt";

        var ext = new string(sanitized, 0, len);
        return ext.Equals("plaintext", StringComparison.OrdinalIgnoreCase) ? ".txt" : $".{ext.ToLowerInvariant()}";
    }
}
