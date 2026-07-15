namespace JoinCode.ChatCommands;

/// <summary>
/// /usage 命令 — 对齐 TS Usage.tsx
/// TS 是 Settings 组件的 Usage 标签页，C# 是终端文本输出
/// 对齐内容：Current session/Current week 限制条+重置时间+Sonnet-only占位+ExtraUsage占位
/// 架构差异：TS 从 Anthropic API 获取 utilization，C# 从本地 RateLimitTracker 获取
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Usage, Description = "查看速率限制用量", Usage = "/usage", Category = ChatCommandCategory.Info)]
public sealed class UsageCommand : IChatCommand
{
    private readonly IClockService _clock = SystemClockService.Instance;
    public string Name => ChatCommandNameConstants.Usage;
    public string Description => "查看速率限制用量";
    public string Usage => "/usage";
    public string[] Aliases => ["rate-limit"];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        // 预收集 Rate Limits 数据
        string rateLimitsContent;

        if (context.Services.RateLimitTracker is not null)
        {
            var snapshot = context.Services.RateLimitTracker.GetLatestSnapshot();
            if (snapshot is not null)
            {
                var sb = new StringBuilder();

                if (snapshot.RequestLimit.HasValue && snapshot.RequestRemaining.HasValue && snapshot.RequestLimit.Value > 0)
                {
                    var usedRequests = snapshot.RequestLimit.Value - snapshot.RequestRemaining.Value;
                    var percentage = Math.Round((double)usedRequests / snapshot.RequestLimit.Value * 100, 1);
                    var resetsAt = snapshot.RequestResetsAt ?? _clock.GetUtcNow().AddHours(5);
                    RenderLimitBar(sb, "Current session", percentage, resetsAt, _clock);
                }

                if (snapshot.TokenLimit.HasValue && snapshot.TokenRemaining.HasValue && snapshot.TokenLimit.Value > 0)
                {
                    var usedTokens = snapshot.TokenLimit.Value - snapshot.TokenRemaining.Value;
                    var percentage = Math.Round((double)usedTokens / snapshot.TokenLimit.Value * 100, 1);
                    var resetsAt = snapshot.TokenResetsAt ?? _clock.GetUtcNow().AddDays(7);
                    RenderLimitBar(sb, "Current week (all models)", percentage, resetsAt, _clock);
                }

                rateLimitsContent = sb.ToString();
            }
            else
            {
                rateLimitsContent = $"  {TerminalColors.Muted}速率限制数据暂不可用{AnsiStyleConstants.Reset}\n  {TerminalColors.Muted}数据将在首次 API 请求后自动填充{AnsiStyleConstants.Reset}";
            }
        }
        else
        {
            rateLimitsContent = $"  {TerminalColors.Muted}速率限制数据暂不可用{AnsiStyleConstants.Reset}\n  {TerminalColors.Muted}数据将在首次 API 请求后自动填充{AnsiStyleConstants.Reset}";
        }

        // 预收集 Token Usage 数据
        string tokenUsageContent;
        if (context.Services.UsageTracker is not null)
        {
            var stats = context.Services.UsageTracker.GetTodayStatistics();
            if (stats.TotalTokens > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{AnsiStyleConstants.Bold}Token Usage (Today){AnsiStyleConstants.Reset}");
                sb.AppendLine();

                RenderTokenBar(sb, "Input", stats.TotalInputTokens, stats.TotalTokens);
                RenderTokenBar(sb, "Output", stats.TotalOutputTokens, stats.TotalTokens);

                sb.Append(TerminalColors.Muted);
                sb.Append($"  Total: {stats.TotalTokens:N0} tokens");
                sb.Append($" · Requests: {stats.TotalRequests}");
                sb.AppendLine(AnsiStyleConstants.Reset);

                if (stats.TotalCacheReadTokens > 0)
                {
                    sb.AppendLine($"{TerminalColors.Muted}  Cache read: {stats.TotalCacheReadTokens:N0} tokens{AnsiStyleConstants.Reset}");
                }

                if (stats.TotalCacheCreationTokens > 0)
                {
                    sb.AppendLine($"{TerminalColors.Muted}  Cache creation: {stats.TotalCacheCreationTokens:N0} tokens{AnsiStyleConstants.Reset}");
                }

                if (stats.TotalCostUsd > 0)
                {
                    sb.AppendLine($"{TerminalColors.Muted}  Cost: ${stats.TotalCostUsd:F4} USD{AnsiStyleConstants.Reset}");
                }

                tokenUsageContent = sb.ToString();
            }
            else
            {
                tokenUsageContent = $"  {TerminalColors.Muted}暂无今日 Token 用量数据{AnsiStyleConstants.Reset}";
            }
        }
        else
        {
            tokenUsageContent = $"  {TerminalColors.Muted}用量追踪器不可用{AnsiStyleConstants.Reset}";
        }

        var panel = new TabPanel(
            ["Rate Limits", "Token Usage"],
            tabIndex => tabIndex switch
            {
                0 => rateLimitsContent,
                1 => tokenUsageContent,
                _ => string.Empty
            });

        return panel.ShowAsync(context.CancellationToken)
            .ContinueWith(_ => ChatCommandResult.Continue(), TaskContinuationOptions.ExecuteSynchronously);
    }

    /// <summary>
    /// 渲染限制进度条 — 对齐 TS Usage.tsx LimitBar
    /// </summary>
    private static void RenderLimitBar(StringBuilder sb, string title, double percentage, DateTime resetsAt, IClockService clock)
    {
        var color = GetLimitColor(percentage);
        var bar = new UsageBar(percentage / 100.0, 30, color, TerminalColors.Muted);

        sb.Append(AnsiStyleConstants.Bold);
        sb.Append($"  {title}");
        sb.Append(AnsiStyleConstants.Reset);
        sb.AppendLine();

        sb.Append("  ");
        sb.Append(bar.Render());

        sb.Append(' ');
        sb.Append(color);
        sb.Append($"{(int)percentage}% used");
        sb.Append(AnsiStyleConstants.Reset);

        // 重置时间 — 对齐 TS "Resets in X"
        var remaining = resetsAt - clock.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            sb.Append(TerminalColors.Muted);
            sb.Append($" · resets in {FormatRemaining(remaining)}");
            sb.Append(AnsiStyleConstants.Reset);
        }

        sb.AppendLine();
        sb.AppendLine();
    }

    /// <summary>
    /// 渲染 Token 用量条 — 对齐 TS Usage.tsx 中的 token 统计
    /// </summary>
    private static void RenderTokenBar(StringBuilder sb, string label, long count, long total)
    {
        if (total <= 0) return;

        var percentage = (double)count / total * 100;
        var color = label == "Input" ? TerminalColors.Primary : TerminalColors.Success;
        var bar = new UsageBar(percentage / 100.0, 30, color, TerminalColors.Muted);

        sb.Append(TerminalColors.Muted);
        sb.Append($"  {label,-8}");
        sb.Append(AnsiStyleConstants.Reset);

        sb.Append(bar.Render());

        sb.Append(' ');
        sb.Append(color);
        sb.Append($"{count,10:N0}");
        sb.Append(AnsiStyleConstants.Reset);
        sb.Append(TerminalColors.Muted);
        sb.Append($" ({percentage,5:F1}%)");
        sb.AppendLine(AnsiStyleConstants.Reset);
    }

    private static string GetLimitColor(double percentage)
    {
        if (percentage >= 90) return TerminalColors.Error;
        if (percentage >= 70) return TerminalColors.Warning;
        return TerminalColors.Success;
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
            return $"{(int)remaining.TotalDays}d {(int)remaining.Hours}h";
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"{remaining.Minutes}m";
    }
}
