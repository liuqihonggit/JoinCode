namespace JoinCode.ChatCommands;

/// <summary>
/// /?? 命令 (别名 /ask) — 需求澄清模式
/// 进入子循环，AI 以产品经理思维用 AskUserQuestion 工具多轮提问，直到需求明确
/// 提示词硬编码在本命令内，不进入 PromptSection 体系
/// </summary>
[ChatCommand(
    Name = ChatCommandNameConstants.AskClarify,
    Description = "需求澄清模式 — AI 多轮提问帮你明确需求",
    Usage = "/?? [需求描述]  或  /ask [需求描述]",
    Category = ChatCommandCategory.Info,
    Aliases = ["ask"],
    ArgumentHint = "[需求描述]")]
public sealed class AskClarifyCommand : ChatCommandBase
{
    /// <summary>
    /// 需求已明确的标记 — LLM 输出此标记时退出澄清循环
    /// </summary>
    internal const string ClarifyDoneMarker = "【需求已明确】";

    /// <summary>
    /// 退出澄清模式的命令
    /// </summary>
    private static readonly string[] ExitCommands = ["/end", "/done", "/exit", "/quit"];

    /// <inheritdoc/>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var chatService = context.Services.ChatService;

        var bufferedOut = TerminalHelper.Out;
        var realStdout = new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding) { AutoFlush = true };
        TerminalHelper.SetOut(realStdout);

        try
        {
            await RunClarifyLoopAsync(chatService, context.Arguments, context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            HandleError("需求澄清", ex);
        }
        finally
        {
            TerminalHelper.SetOut(bufferedOut);
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 澄清子循环 — 发送消息、消费事件、检查退出条件
    /// </summary>
    private static async Task RunClarifyLoopAsync(IChatService chatService, string initialArgs, CancellationToken ct)
    {
        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{TerminalColors.Accent}{AnsiStyleConstants.Bold}╔══ 需求澄清模式 ══╗{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"{TerminalColors.Accent}║ AI 会多轮提问帮你明确需求  ║{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"{TerminalColors.Accent}║ 输入 {AnsiStyleConstants.Bold}/end{AnsiStyleConstants.Reset}{TerminalColors.Accent} 退出  需求明确后自动结束 ║{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"{TerminalColors.Accent}╚════════════════════╝{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine();

        var isFirstRound = true;
        var currentInput = initialArgs.Trim();

        while (!ct.IsCancellationRequested)
        {
            if (string.IsNullOrEmpty(currentInput))
            {
                TerminalHelper.WriteRaw($"{AnsiStyleConstants.Dim}请描述你的需求(或输入 /end 退出): {AnsiStyleConstants.Reset}");
                currentInput = TerminalHelper.ReadLine().Trim();
                if (string.IsNullOrEmpty(currentInput) || IsExitCommand(currentInput))
                {
                    TerminalHelper.WriteLine($"{AnsiStyleConstants.Dim}已退出澄清模式。{AnsiStyleConstants.Reset}");
                    return;
                }
            }

            var message = isFirstRound
                ? $"{AskClarifyPrompts.SystemPrompt}\n\n---\n\n用户需求: {currentInput}"
                : currentInput;

            TerminalHelper.WriteLine();
            TerminalHelper.WriteLine($"{AnsiStyleConstants.Dim}━━ AI 分析中 ━━{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine();

            var responseBuilder = new StringBuilder();
            var clarifyDone = false;

            await foreach (var evt in chatService.StreamWithEventsAsync(message, ct).ConfigureAwait(false))
            {
                evt.Switch(
                    onText: content =>
                    {
                        if (content.Length > 0)
                        {
                            responseBuilder.Append(content);
                            TerminalHelper.WriteRaw(content);
                        }
                    },
                    onThinking: _ => { },
                    onToolStart: (toolName, _, _) =>
                    {
                        TerminalHelper.WriteLine();
                        TerminalHelper.WriteLine($"{AnsiStyleConstants.Dim}  [工具] {toolName}{AnsiStyleConstants.Reset}");
                    },
                    onToolEnd: (toolName, resultText, _, isToolError, _) =>
                    {
                        if (isToolError)
                            TerminalHelper.WriteLine($"  {TerminalColors.Error}工具错误: {Truncate(resultText, 200)}{AnsiStyleConstants.Reset}");
                    },
                    onToolProgress: (_, _, _) => { },
                    onLoopDetected: (_, _, _) => { },
                    onTimingSummary: _ => { },
                    onDone: (_, _) => { });
            }

            var response = responseBuilder.ToString();
            TerminalHelper.NewLine();

            if (response.Contains(ClarifyDoneMarker, StringComparison.OrdinalIgnoreCase))
            {
                clarifyDone = true;
                TerminalHelper.WriteLine();
                TerminalHelper.WriteLine($"{TerminalColors.Accent}{AnsiStyleConstants.Bold}✓ 需求澄清完成{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine($"{AnsiStyleConstants.Dim}以上是明确后的需求总结,可以直接开始开发。{AnsiStyleConstants.Reset}");
                TerminalHelper.WriteLine();
                return;
            }

            if (clarifyDone)
                return;

            isFirstRound = false;
            currentInput = string.Empty;

            TerminalHelper.WriteLine();
            TerminalHelper.WriteLine($"{AnsiStyleConstants.Dim}AI 还在澄清中,你可以补充信息或直接回车结束: {AnsiStyleConstants.Reset}");
        }
    }

    /// <summary>
    /// 判断是否为退出命令
    /// </summary>
    internal static bool IsExitCommand(string input)
    {
        var trimmed = input.Trim().ToLowerInvariant();
        return ExitCommands.Any(c => trimmed == c);
    }

    /// <summary>
    /// 截断文本用于显示
    /// </summary>
    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), "...");
    }
}

/// <summary>
/// 需求澄清提示词 — 硬编码在命令内,不进入 PromptSection 体系
/// </summary>
internal static class AskClarifyPrompts
{
    /// <summary>
    /// 澄清模式系统提示词 — 作为第一条消息前缀发送给 LLM
    /// </summary>
    internal const string SystemPrompt = """
【需求澄清模式】
你是一个专业的编程助手,你需要协助用户提供专业知识,每次回答的时候要附带解释选型的好坏,让用户做选择题而不是问答题.
如果涉及代码功能设计,按照鱼骨图思路展开,从难点开始分解,每次用 AskUserQuestion 工具询问用户选择.

请你把用户当成什么都不懂的新手,要求可能很模糊,也可能不准确,甚至会出现一些专业性的错误,你要以产品经理的思维先去理解用户的需求,请你根据自己的判断协助用户完成项目.

【澄清规则】
1. 你现在处于需求澄清模式,目标是帮助用户明确需求,而不是直接写代码
2. 每次回应优先使用 AskUserQuestion 工具向用户提问,提供2-4个选项让用户选择
3. 每个选项必须附带解释选型的好坏,让用户做选择题而非问答题
4. 涉及代码功能设计时,按鱼骨图思路展开:从主干目标开始,逐层分解到子问题,从难点开始突破
5. 当你认为需求已经足够明确时,输出"【需求已明确】"标记,然后总结需求清单
6. 把用户当成什么都不懂的新手,以产品经理思维理解需求,主动发现需求中的矛盾和遗漏
""";
}
