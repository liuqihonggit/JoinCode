namespace JoinCode.ChatCommands;

/// <summary>
/// /btw 命令 — 对齐 TS btw.ts
/// TS 使用 sideQuestion 模式发送独立请求，C# 使用 ChatService.SendMessageAsync
/// 对齐内容：侧边提问不影响主对话上下文
/// 架构差异：TS 有 React 侧边栏渲染，C# 为终端文本输出
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Btw, Description = "快速向 AI 提一个侧边问题", Usage = "/btw <question>", Category = ChatCommandCategory.Social)]
public sealed class BtwCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.Btw;
    public string Description => "快速向 AI 提一个侧边问题";
    public string Usage => "/btw <question>";
    public string[] Aliases => [];
    public string ArgumentHint => "<question>";
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var question = ChatCommandBase.GetNormalizedArgs(context);

        if (string.IsNullOrEmpty(question))
        {
            TerminalHelper.WriteLine("用法: /btw <question>");
            TerminalHelper.WriteLine("快速向 AI 提一个侧边问题，不影响当前对话上下文");
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("示例:");
            TerminalHelper.WriteLine("  /btw 什么是 SOLID 原则?");
            TerminalHelper.WriteLine("  /btw 这个函数的时间复杂度是多少?");
            return ChatCommandResult.Continue();
        }

        var prompt = $"""
This is a quick side question that should not disrupt the main conversation context. Answer concisely.

Question: {question}
""";

        try
        {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}── 侧边问题 ──{AnsiStyleConstants.Reset}");
            var result = await context.Services.ChatService.SendMessageAsync(prompt, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine(result);
            TerminalHelper.WriteLine($"{TerminalColors.Muted}── 侧边回答结束 ──{AnsiStyleConstants.Reset}");
        }
        catch (Exception ex)
        {
            ChatCommandBase.HandleError("侧边提问", ex);
        }

        return ChatCommandResult.Continue();
    }
}
