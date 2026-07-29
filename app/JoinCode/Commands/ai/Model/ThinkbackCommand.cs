
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Thinkback, Description = "回放 AI 的思考过程", Usage = "/thinkback [count]", Category = ChatCommandCategory.Model, ArgumentHint = "[count]")]
public sealed class ThinkbackCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        if (context.Services.ThinkingStore is null)
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}思考存储不可用{AnsiStyleConstants.Reset}");
            return ChatCommandResult.Continue();
        }

        var count = 1;
        if (!string.IsNullOrEmpty(ChatCommandBase.GetNormalizedArgs(context)) && int.TryParse(ChatCommandBase.GetNormalizedArgs(context), out var n))
        {
            count = Math.Max(1, n);
        }

        var entries = await context.Services.ThinkingStore.GetRecentAsync(context.SessionId, count, context.CancellationToken).ConfigureAwait(false);

        TerminalHelper.WriteLine("思考回放:");
        TerminalHelper.NewLine();

        if (entries.Count == 0)
        {
            TerminalHelper.WriteLine("  当前会话暂无思考过程记录");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("  当 AI 使用 extended thinking 时将自动记录");
        }
        else
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var index = entries.Count - i;
                TerminalHelper.WriteLine($"  ── 思考 #{index} ──");
                if (!string.IsNullOrEmpty(entry.ModelId))
                {
                    TerminalHelper.WriteLine($"  模型: {entry.ModelId}");
                }
                TerminalHelper.WriteLine($"  时间: {entry.Timestamp:yyyy-MM-dd HH:mm:ss UTC}");
                TerminalHelper.NewLine();

                var lines = entry.Content.Split('\n');
                foreach (var line in lines)
                {
                    TerminalHelper.WriteLine($"{AnsiStyleConstants.Dim}{AnsiStyleConstants.Italic}  {line}{AnsiStyleConstants.Reset}");
                }

                TerminalHelper.NewLine();
            }

            TerminalHelper.WriteLine($"  共 {entries.Count} 条思考记录");
        }

        TerminalHelper.NewLine();
        return ChatCommandResult.Continue();
    }
}
