namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Summary, Description = "显示当前会话摘要", Usage = "/summary", Category = ChatCommandCategory.System)]
public sealed class SummaryCommand : IChatCommand
{
    private readonly IClockService _clock = SystemClockService.Instance;
    public string Name => ChatCommandNameConstants.Summary;
    public string Description => "显示当前会话摘要";
    public string Usage => "/summary";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        TerminalHelper.WriteLine($"{TerminalColors.Primary}会话摘要{AnsiStyleConstants.Reset}");
        TerminalHelper.NewLine();

        try
        {
            var history = await context.Services.ChatService.GetMessageListAsync(context.CancellationToken);

            if (history.Count == 0)
            {
                TerminalHelper.WriteLine($"  {TerminalColors.Muted}暂无对话记录{AnsiStyleConstants.Reset}");
                return ChatCommandResult.Continue();
            }

            var userMessages = history.Where(m =>
                string.Equals(m.Role, MessageRoleConstants.User, StringComparison.OrdinalIgnoreCase)).ToList();
            var assistantMessages = history.Where(m =>
                string.Equals(m.Role, MessageRoleConstants.Assistant, StringComparison.OrdinalIgnoreCase)).ToList();

            var duration = _clock.GetUtcNow() - context.SessionStartedAt;

            TerminalHelper.WriteLine($"  会话ID: {context.SessionId}");
            TerminalHelper.WriteLine($"  持续时间: {FormatDuration(duration)}");
            TerminalHelper.WriteLine($"  总消息数: {history.Count}");
            TerminalHelper.WriteLine($"  用户消息: {userMessages.Count}");
            TerminalHelper.WriteLine($"  AI回复:   {assistantMessages.Count}");
            TerminalHelper.NewLine();

            TerminalHelper.WriteLine($"{TerminalColors.Accent}最近对话:{AnsiStyleConstants.Reset}");
            var recentMessages = history.TakeLast(6);
            foreach (var msg in recentMessages)
            {
                var roleLabel = string.Equals(msg.Role, MessageRoleConstants.User, StringComparison.OrdinalIgnoreCase)
                    ? $"{TerminalColors.Primary}你{AnsiStyleConstants.Reset}"
                    : $"{TerminalColors.Success}AI{AnsiStyleConstants.Reset}";

                var preview = msg.Content;
                if (preview.Length > 100)
                    preview = preview[..97] + "...";

                TerminalHelper.WriteLine($"  {roleLabel}: {preview}");
            }

            if (history.Count > 6)
            {
                TerminalHelper.WriteLine($"  {TerminalColors.Muted}... 还有 {history.Count - 6} 条消息{AnsiStyleConstants.Reset}");
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