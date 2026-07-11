namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Review, Description = "审查 Pull Request 或代码变更", Usage = "/review [pr-number]", Category = ChatCommandCategory.Code)]
public sealed class ReviewCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Review;
    public string Description => "审查 Pull Request 或代码变更";
    public string Usage => "/review [pr-number]";
    public string[] Aliases => [];
    public string ArgumentHint => "[pr-number]";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context);

        // 无参数时：交互式选择审查方式
        // 对齐 TS: UltrareviewOverageDialog — 审查选项选择
        if (string.IsNullOrEmpty(args) && !Core.Utils.TestEnvironmentDetector.IsNonInteractive && context.Services?.ChatService is not null)
        {
            var dialog = new Dialog("代码审查", "选择审查方式:", ["审查本地变更", "审查 Pull Request", "取消"]);
            var result = await dialog.ShowAsync(context.CancellationToken).ConfigureAwait(false);

            if (result.Cancelled || result.SelectedIndex == 2)
            {
                TerminalHelper.WriteLine("已取消");
                return ChatCommandResult.Continue();
            }

            var prompt = result.SelectedIndex == 0 ? BuildPrompt(string.Empty) : BuildPrompt("list");
            await RunReviewAsync(context, prompt).ConfigureAwait(false);
            return ChatCommandResult.Continue();
        }

        var reviewPrompt = BuildPrompt(args);
        await RunReviewAsync(context, reviewPrompt).ConfigureAwait(false);
        return ChatCommandResult.Continue();
    }

    private static async Task RunReviewAsync(ChatCommandContext context, string prompt)
    {
        try
        {
            if (context.Services?.ChatService is null)
            {
                TerminalHelper.WriteLine("ChatService 不可用，无法执行审查。");
                return;
            }

            TerminalHelper.WriteLine($"{TerminalColors.Primary}正在审查...{AnsiStyleConstants.Reset}");
            TerminalHelper.NewLine();

            // 对齐 TS: type='prompt'，结果直接进入对话上下文
            // 使用 SendMessageStreamAsync 让 LLM 回复流式输出到终端
            await foreach (var _ in context.Services.ChatService.SendMessageStreamAsync(prompt, context.CancellationToken).ConfigureAwait(false))
            {
                // 流式输出由 ChatService 内部处理
            }
        }
        catch (OperationCanceledException)
        {
            TerminalHelper.WriteLine("审查已取消。");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("审查", ex);
        }
    }

    /// <summary>
    /// 构建 prompt — 对齐 TS review.ts getPromptForCommand
    /// TS 端是纯 prompt 类型，统一模板通过 args 动态插入 PR 号
    /// </summary>
    private static string BuildPrompt(string args)
    {
        if (!string.IsNullOrEmpty(args) && int.TryParse(args, out _))
        {
            // 有 PR 号 — 对齐 TS: 指示 LLM 运行 gh pr view/diff
            return $"""
                You are an expert code reviewer. Follow these steps:

                1. Run `gh pr view {args}` to get PR details
                2. Run `gh pr diff {args}` to get the diff
                3. Analyze the changes and provide a thorough code review that includes:
                   - Overview of what the PR does
                   - Analysis of code quality and style
                   - Specific suggestions for improvements
                   - Any potential issues or risks

                Keep your review concise but thorough. Focus on:
                - Code correctness
                - Following project conventions
                - Performance implications
                - Test coverage
                - Security considerations

                Format your review with clear sections and bullet points.

                PR number: {args}
                """;
        }

        // 无参数 — 对齐 TS: 指示 LLM 运行 gh pr list 列出 PR
        // C# 扩展: 也支持本地变更审查
        return """
            You are an expert code reviewer. Follow these steps:

            1. If no PR number is provided, run `gh pr list` to show open PRs
            2. If a PR number is provided, run `gh pr view <number>` to get PR details
            3. Run `gh pr diff <number>` to get the diff
            4. Analyze the changes and provide a thorough code review that includes:
               - Overview of what the PR does
               - Analysis of code quality and style
               - Specific suggestions for improvements
               - Any potential issues or risks

            Keep your review concise but thorough. Focus on:
            - Code correctness
            - Following project conventions
            - Performance implications
            - Test coverage
            - Security considerations

            Format your review with clear sections and bullet points.
            """;
    }
}
