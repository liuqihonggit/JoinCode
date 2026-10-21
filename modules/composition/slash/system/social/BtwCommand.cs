namespace JoinCode.ChatCommands;

/// <summary>
/// /btw 命令 — 对齐 TS btw.ts + sideQuestion.ts + forkedAgent.ts：
/// TS 用 runForkedAgent（隔离轻量 fork 代理：父上下文消息做前缀共享缓存、独立实例、
/// canUseTool 全拒、maxTurns=1、不写回父上下文）；C# 照抄同语义 — AgentBase +
/// Options.InitialMessageList（主历史副本 + 问题）+ MaxIterations=1 + ContextManager 不注入
///（天然隔离：不碰主上下文状态、不写回历史）。修复背景：原实现走 ChatService 共享管道，
/// 受主对话会话态影响偶发空响应（CLI/探针实证链路本身健康，共享态是架构 gap）。
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Btw, Description = "快速向 AI 提一个侧边问题", Usage = "/btw <question>", Category = ChatCommandCategory.Social, ArgumentHint = "<question>")]
[ChatCommandArg("question", Type = "string", Description = "侧边问题内容", Required = true)]
public sealed class BtwCommand : ChatCommandBase {
    /// <summary>侧边问题 system-reminder 模板（对齐 TS runSideQuestion wrappedQuestion：隔离代理身份 + 无工具约束 + 单次回复）。</summary>
    private const string SideQuestionReminder = """
此为用户的侧边问题，请直接在单次回复中回答。

重要背景：
- 你是为回答这一个问题而生成的独立轻量代理
- 主对话代理未被中断，仍在后台独立运行
- 你共享对话上下文，但是完全独立的实例
- 不要提及被打断或"之前正在做什么"——此表述不正确

关键约束：
- 你没有任何可用工具——不能读文件、执行命令、搜索或采取任何行动
- 这是一次性回复——没有后续轮次
- 只能基于对话上下文中已有的信息作答
- 绝不说"让我试试"、"我将检查"或承诺采取任何行动
- 不知道答案就直说——不要提出去查证或调查

请直接用你已有的信息回答问题。
""";

    /// <summary>
    /// 执行 /btw 命令 — 隔离 fork 代理回答侧边问题，不影响主对话上下文（对齐 TS runSideQuestion）
    /// </summary>
    /// <param name="context">命令执行上下文，包含问题文本与取消令牌</param>
    /// <returns>命令执行结果（始终为 Continue，表示不中断主对话流）</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var question = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(question)) {
            TerminalHelper.WriteLine("用法: /btw <question>");
            TerminalHelper.WriteLine("快速向 AI 提一个侧边问题，不影响当前对话上下文");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("示例:");
            TerminalHelper.WriteLine("  /btw 什么是 SOLID 原则?");
            TerminalHelper.WriteLine("  /btw 这个函数的时间复杂度是多少?");
            return ChatCommandResult.Continue();
        }

        try {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}── 侧边问题 ──{AnsiStyleEnumConstants.Reset}");
            var response = await RunSideQuestionAsync(context, question).ConfigureAwait(false);
            TerminalHelper.WriteLine(response);
            TerminalHelper.WriteLine($"{TerminalColors.Muted}── 侧边回答结束 ──{AnsiStyleEnumConstants.Reset}");
        } catch (Exception ex) {
            ChatCommandBase.HandleError("侧边提问", ex);
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>隔离 fork 侧边问题（对齐 TS runSideQuestion→runForkedAgent）：
    /// 主历史副本做前缀（共享上下文）+ system-reminder 包裹问题 + 独立 AgentBase 单轮执行
    ///（MaxIterations=1 对齐 maxTurns:1；ContextManager 不注入 = 不写回主上下文天然隔离）。</summary>
    internal static async Task<string> RunSideQuestionAsync(ChatCommandContext context, string question) {
        var provider = context.GetCommandServices().ServiceProvider;
        var queryEngine = provider?.GetService<IQueryEngine>()
            ?? throw new InvalidOperationException("IQueryEngine 未注册 — 无法执行侧边问题 fork 调用");

        // fork 前缀 — 主上下文消息副本（隔离：追加问题不写回主历史）
        var forkHistory = new MessageList();
        var contextManager = provider?.GetService<IChatContextManager>();
        if (contextManager is not null) {
            var parent = await contextManager.GetMessageListAsync(context.CancellationToken).ConfigureAwait(false);
            foreach (var message in parent) {
                forkHistory.Add(message);
            }
        }

        forkHistory.AddUserMessage($"{SideQuestionReminder}\n\n{question}");

        await using var agent = new AgentBase(
            task: question,
            options: new SubAgentOptions {
                InitialMessageList = forkHistory,
                MaxIterations = 1,
            },
            queryEngine: queryEngine,
            logger: provider?.GetService<ILogger<AgentBase>>(),
            name: "side-question");

        var response = new StringBuilder();
        await foreach (var chunk in agent.ExecuteStreamAsync(context.CancellationToken).ConfigureAwait(false)) {
            if (chunk.Type == AgentStreamChunkType.Content && chunk.Content is { Length: > 0 }) {
                response.Append(chunk.Content);
            }
        }

        return response.ToString();
    }
}
