namespace JoinCode.ChatCommands;

/// <summary>
/// /rewind 命令 - 撤回对话历史
/// 对齐 TS: src/commands/rewind/rewind.ts
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Rewind, Description = "恢复代码和/或对话到之前的状态", Usage = "/rewind [last|<n>|all]", Aliases = ["checkpoint"], Category = ChatCommandCategory.Session)]
public sealed class RewindCommand : ChatCommandBase
{
    public override string Name => ChatCommandNameConstants.Rewind;
    public override string Description => "恢复代码和/或对话到之前的状态";
    public override string Usage => "/rewind [last|<n>|all]";
    public override string[] Aliases => ["checkpoint"];
    public override string ArgumentHint => "[last|<n>|all]";

    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var arg = GetNormalizedArgs(context).ToLowerInvariant();

        RewindResult result;

        if (string.IsNullOrEmpty(arg) || arg == "last")
        {
            result = await context.Services.ChatService.RewindLastTurnAsync(context.CancellationToken).ConfigureAwait(false);
            PrintResult("最后一轮对话 (SP-3)", result);
        }
        else if (arg == "all")
        {
            result = await context.Services.ChatService.RewindToStartAsync(context.CancellationToken).ConfigureAwait(false);
            PrintResult("会话初始状态 (SP-0)", result);
        }
        else if (int.TryParse(arg, out var index))
        {
            result = await context.Services.ChatService.RewindToMessageIndexAsync(index, context.CancellationToken).ConfigureAwait(false);
            PrintResult($"消息索引 {index} (SP-5)", result);
        }
        else
        {
            TerminalHelper.WriteLine($"未知参数: {context.Arguments}");
            TerminalHelper.WriteLine("用法: /rewind [last|<n>|all]");
            TerminalHelper.WriteLine("  last — 撤回最后一轮对话（默认）");
            TerminalHelper.WriteLine("  <n>  — 撤回到第 n 条消息");
            TerminalHelper.WriteLine("  all  — 撤回全部对话历史");
            return ChatCommandResult.Continue();
        }

        return ChatCommandResult.Continue();
    }

    private static void PrintResult(string target, RewindResult result)
    {
        if (result.Success)
        {
            TerminalHelper.WriteLine($"已撤回到 {target}：移除 {result.RemovedCount} 条消息，剩余 {result.RemainingCount} 条");
        }
        else
        {
            TerminalHelper.WriteLine($"撤回失败：{result.ErrorMessage}");
        }
    }
}
