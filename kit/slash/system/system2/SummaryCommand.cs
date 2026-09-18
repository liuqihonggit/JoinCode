namespace JoinCode.ChatCommands;

/// <summary>
/// /summary 命令 — 显示当前会话摘要
/// 输出会话 ID、持续时间、消息统计及最近 6 条对话预览
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Summary, Description = "显示当前会话摘要", Usage = "/summary", Category = ChatCommandCategory.System)]
public sealed class SummaryCommand : ChatCommandBase
{
    private readonly IClockService _clock = SystemClockService.Instance;
    /// <summary>
    /// 执行 /summary 命令 — 收集对话历史并输出会话统计与最近消息预览
    /// </summary>
    /// <param name="context">命令执行上下文，包含会话 ID、开始时间、取消令牌等</param>
    /// <returns>命令执行结果（始终为 Continue，表示不中断主对话流）</returns>
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        TerminalHelper.WriteLine($"{TerminalColors.Primary}会话摘要{AnsiStyleEnumConstants.Reset}");
        TerminalHelper.NewLine();

        try
        {
            var history = await context.GetCommandServices().ChatService.GetMessageListAsync(context.CancellationToken);

            if (history.Count == 0)
            {
                TerminalHelper.WriteLine($"  {TerminalColors.Muted}暂无对话记录{AnsiStyleEnumConstants.Reset}");
                return ChatCommandResult.Continue();
            }

            var userMessages = history.Where(m =>
                string.Equals(m.Role, MessageRoleEnumConstants.User, StringComparison.OrdinalIgnoreCase)).ToList();
            var assistantMessages = history.Where(m =>
                string.Equals(m.Role, MessageRoleEnumConstants.Assistant, StringComparison.OrdinalIgnoreCase)).ToList();

            var duration = _clock.GetUtcNow() - context.SessionStartedAt;

            TerminalHelper.WriteLine($"  会话ID: {context.SessionId}");
            TerminalHelper.WriteLine($"  持续时间: {FormatDuration(duration)}");
            TerminalHelper.WriteLine($"  总消息数: {history.Count}");
            TerminalHelper.WriteLine($"  用户消息: {userMessages.Count}");
            TerminalHelper.WriteLine($"  AI回复:   {assistantMessages.Count}");
            TerminalHelper.NewLine();

            TerminalHelper.WriteLine($"{TerminalColors.Accent}最近对话:{AnsiStyleEnumConstants.Reset}");
            var recentMessages = history.TakeLast(6);
            foreach (var msg in recentMessages)
            {
                var roleLabel = string.Equals(msg.Role, MessageRoleEnumConstants.User, StringComparison.OrdinalIgnoreCase)
                    ? $"{TerminalColors.Primary}你{AnsiStyleEnumConstants.Reset}"
                    : $"{TerminalColors.Success}AI{AnsiStyleEnumConstants.Reset}";

                var preview = msg.Content;
                if (preview.Length > 100)
                    preview = preview[..97] + "...";

                TerminalHelper.WriteLine($"  {roleLabel}: {preview}");
            }

            if (history.Count > 6)
            {
                TerminalHelper.WriteLine($"  {TerminalColors.Muted}... 还有 {history.Count - 6} 条消息{AnsiStyleEnumConstants.Reset}");
            }
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("生成摘要", ex);
        }

        return ChatCommandResult.Continue();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
            return $"{duration.Seconds}秒";
        if (duration.TotalHours < 1)
            return $"{duration.Minutes}分钟";
        if (duration.TotalDays < 1)
            return $"{duration.Hours}小时{duration.Minutes}分钟";

        return $"{duration.Days}天{duration.Hours}小时";
    }
}