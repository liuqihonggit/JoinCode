namespace JoinCode.Cli;

// ─── TerminalCharts ───

/// <summary>
/// 终端图表 — CLI 简化版
/// </summary>
public static class TerminalCharts {
    /// <summary>
    /// 渲染每日活动热力图为竖直方块条形图
    /// </summary>
    /// <param name="activities">每日活动只读列表</param>
    /// <param name="title">可选标题</param>
    /// <returns>渲染后的文本，无活动时返回空文本或仅标题</returns>
    public static string ActivityHeatmap(IReadOnlyList<DailyActivity> activities, string? title = null) {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(title)) {
            sb.AppendLine($"{AnsiStyleEnumConstants.Bold}{title}{AnsiStyleEnumConstants.Reset}");
            sb.AppendLine();
        }

        if (activities.Count == 0) return sb.ToString();

        var maxCount = activities.Max(a => a.MessageCount);
        if (maxCount == 0) maxCount = 1;

        var blocks = new[] { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

        sb.Append("  ");
        foreach (var day in activities) {
            var idx = (int)Math.Round((double)day.MessageCount / maxCount * (blocks.Length - 1));
            if (idx < 0) idx = 0;
            if (idx >= blocks.Length) idx = blocks.Length - 1;
            sb.Append($"{TerminalColors.Primary}{blocks[idx]}{AnsiStyleEnumConstants.Reset}");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// 根据总 Token 数生成趣味事实文本，Token 不足百万时返回空字符串
    /// </summary>
    /// <param name="totalTokens">总 Token 数</param>
    /// <param name="daysActive">活跃天数</param>
    /// <param name="totalHours">总小时数</param>
    /// <returns>趣味事实文本，不满足条件时返回空字符串</returns>
    public static string FunFactoid(long totalTokens, int daysActive, double totalHours) {
        if (totalTokens < 1000) return "";
        var tokensInMillions = totalTokens / 1_000_000.0;
        if (tokensInMillions > 1) {
            return $"That's about {tokensInMillions:F1}M tokens — equivalent to reading ~{tokensInMillions * 0.5:F0} books!";
        }
        return "";
    }
}