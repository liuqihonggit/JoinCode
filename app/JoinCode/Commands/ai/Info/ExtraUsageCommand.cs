
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.ExtraUsage, Description = "查看额外用量信息", Usage = "/extra-usage", Category = ChatCommandCategory.Model, IsHidden = true)]
public sealed class ExtraUsageCommand : ChatCommandBase
{
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var usageTracker = context.Services.UsageTracker;

        TerminalHelper.WriteLine("额外用量:");
        TerminalHelper.NewLine();

        if (usageTracker is not null)
        {
            try
            {
                var todayStats = usageTracker.GetTodayStatistics();
                var totalStats = usageTracker.GetTotalStatistics();

                TerminalHelper.WriteLine("  今日统计:");
                TerminalHelper.WriteLine($"    请求数: {todayStats.TotalRequests}");
                TerminalHelper.WriteLine($"    输入 Token: {todayStats.TotalInputTokens:N0}");
                TerminalHelper.WriteLine($"    输出 Token: {todayStats.TotalOutputTokens:N0}");
                TerminalHelper.WriteLine($"    估算成本: ${todayStats.TotalCostUsd:F4}");

                TerminalHelper.NewLine();
                TerminalHelper.WriteLine("  累计统计:");
                TerminalHelper.WriteLine($"    总请求数: {totalStats.TotalRequests}");
                TerminalHelper.WriteLine($"    总输入 Token: {totalStats.TotalInputTokens:N0}");
                TerminalHelper.WriteLine($"    总输出 Token: {totalStats.TotalOutputTokens:N0}");
                TerminalHelper.WriteLine($"    总估算成本: ${totalStats.TotalCostUsd:F4}");
            }
            catch (Exception ex)
            {
                ChatCommandBase.HandleError("获取用量数据", ex);
            }
        }
        else
        {
            TerminalHelper.WriteLine("  用量追踪器未初始化");
        }

        return Task.FromResult(ChatCommandResult.Continue());
    }
}
