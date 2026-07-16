
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.RateLimitOptions, Description = "配置速率限制选项", Usage = "/rate-limit-options [show]", Category = ChatCommandCategory.Model, Aliases = ["rate-limit"], ArgumentHint = "[show]", IsHidden = true)]
public sealed class RateLimitCommand : ChatCommandBase
{
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context);
        var tracker = context.Services.RateLimitTracker;

        if (string.IsNullOrEmpty(args) || args.Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            TerminalHelper.WriteLine("速率限制:");

            if (tracker is not null)
            {
                var snapshot = tracker.GetLatestSnapshot();
                if (snapshot is not null)
                {
                    TerminalHelper.WriteLine($"  请求限制: {FormatNullable(snapshot.RequestLimit)}");
                    TerminalHelper.WriteLine($"  请求剩余: {FormatNullable(snapshot.RequestRemaining)}");
                    TerminalHelper.WriteLine($"  请求重置: {FormatDateTime(snapshot.RequestResetsAt)}");
                    TerminalHelper.WriteLine($"  Token 限制: {FormatNullable(snapshot.TokenLimit)}");
                    TerminalHelper.WriteLine($"  Token 剩余: {FormatNullable(snapshot.TokenRemaining)}");
                    TerminalHelper.WriteLine($"  Token 重置: {FormatDateTime(snapshot.TokenResetsAt)}");
                    TerminalHelper.WriteLine($"  捕获时间: {snapshot.CapturedAt:HH:mm:ss}");
                }
                else
                {
                    TerminalHelper.WriteLine("  暂无速率限制数据");
                    TerminalHelper.WriteLine("  数据将在下次 API 请求后更新");
                }
            }
            else
            {
                TerminalHelper.WriteLine("  速率限制追踪器未初始化");
            }
        }
        else
        {
            TerminalHelper.WriteLine($"未知操作: {args}");
            TerminalHelper.WriteLine("支持: show");
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }

    private static string FormatNullable(int? value) => value?.ToString("N0") ?? "无限制";
    private static string FormatDateTime(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知";
}
