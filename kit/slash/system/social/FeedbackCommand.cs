namespace JoinCode.ChatCommands;

/// <summary>
/// /feedback 命令 — 提交用户反馈
/// 将反馈文本经 FeedbackRedactor 脱敏后保存到 feedback 目录
/// 支持别名 /bug
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Feedback, Description = "提交反馈", Usage = "/feedback [反馈内容]", Category = ChatCommandCategory.Social, Aliases = ["bug"], ArgumentHint = "[反馈内容]", IsHidden = true)]
[ChatCommandArg("feedback", Type = "string", Description = "反馈内容文本")]
public sealed class FeedbackCommand : ChatCommandBase
{
    /// <summary>
    /// 执行 /feedback 命令 — 无参数时显示输入引导，有参数时脱敏并保存反馈
    /// </summary>
    /// <param name="context">命令执行上下文，包含反馈文本与取消令牌</param>
    /// <returns>命令执行结果（始终为 Continue，表示不中断主对话流）</returns>
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var feedbackText = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrWhiteSpace(feedbackText))
        {
            var userInputState = new FeedbackState
            {
                Step = FeedbackStep.UserInput,
                Description = "",
            };

            TerminalHelper.WriteLine(new FeedbackRenderer().Render(userInputState));
            return ChatCommandResult.Continue();
        }

        var appDataPath = AppDataConstants.Paths.JccDirectory;
        var feedbackDir = Path.Combine(appDataPath, "feedback");
        var fs = context.GetCommandServices().FileSystem;
        DirectoryHelper.EnsureDirectoryExists(fs, feedbackDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"feedback_{timestamp}.md";
        var filePath = Path.Combine(feedbackDir, fileName);

        var redactedText = FeedbackRedactor.Redact(feedbackText);
        var content = $"# 反馈\n\n**时间**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n{redactedText}\n";
        await fs.WriteAllTextAsync(filePath, content, context.CancellationToken).ConfigureAwait(false);

        var state = new FeedbackState
        {
            Step = FeedbackStep.Done,
            Description = feedbackText,
            IsSuccess = true,
        };

        TerminalHelper.WriteLine(new FeedbackRenderer().Render(state));
        TerminalHelper.WriteLine($"  {TerminalColors.Muted}已保存到: {filePath}{AnsiStyleConstants.Reset}");

        return ChatCommandResult.Continue();
    }
}
