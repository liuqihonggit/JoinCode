namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Feedback, Description = "提交反馈", Usage = "/feedback [反馈内容]", Category = ChatCommandCategory.Social)]
public sealed class FeedbackCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Feedback;
    public string Description => "提交反馈";
    public string Usage => "/feedback [反馈内容]";
    public string[] Aliases => ["bug"];
    public string ArgumentHint => "[反馈内容]";
    public bool IsHidden => true;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
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

        var appDataPath = WorkflowConstants.Paths.JccDirectory;
        var feedbackDir = Path.Combine(appDataPath, "feedback");
        var fs = context.Services.FileSystem;
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
