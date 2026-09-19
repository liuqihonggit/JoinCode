namespace JoinCode.Cli;

/// <summary>
/// 统计渲染器 — 纯文本终端输出
/// </summary>
public sealed class StatsRenderer {
    /// <summary>
    /// 渲染统计数据为纯文本终端输出
    /// </summary>
    /// <param name="data">统计数据源</param>
    /// <param name="tab">当前激活的标签页（默认 Overview）</param>
    /// <returns>渲染后的纯文本字符串</returns>
    public string Render(StatsData data, StatsTab tab = StatsTab.Overview) {
        var sb = new StringBuilder();

        RenderTabHeader(sb, tab, data);

        switch (tab) {
            case StatsTab.Overview:
            RenderOverview(sb, data);
            break;
            case StatsTab.Models:
            RenderModels(sb, data);
            break;
            case StatsTab.Daily:
            RenderDaily(sb, data);
            break;
        }

        return sb.ToString();
    }

    private static void RenderTabHeader(StringBuilder sb, StatsTab activeTab, StatsData data) {
        var range = "";
        if (data.DateRangeStart.HasValue && data.DateRangeEnd.HasValue) {
            range = $" ({data.DateRangeStart.Value:MMM d} - {data.DateRangeEnd.Value:MMM d})";
        }

        sb.AppendLine($"{AnsiStyleEnumConstants.Bold}Stats{range}{AnsiStyleEnumConstants.Reset}");
        sb.AppendLine();

        var tabs = new[] { ("Overview", StatsTab.Overview), ("Models", StatsTab.Models), ("Daily", StatsTab.Daily) };
        var tabParts = new List<string>();
        foreach (var (label, t) in tabs) {
            if (t == activeTab) {
                tabParts.Add($"{TerminalColors.Accent}{AnsiStyleEnumConstants.Bold}{label}{AnsiStyleEnumConstants.Reset}");
            } else {
                tabParts.Add($"{AnsiStyleEnumConstants.Dim}{label}{AnsiStyleEnumConstants.Reset}");
            }
        }
        sb.Append("  ");
        sb.AppendLine(string.Join($" {TerminalColors.Muted}│{AnsiStyleEnumConstants.Reset} ", tabParts));
        sb.Append($"  {TerminalColors.Muted}{new string('─', 40)}{AnsiStyleEnumConstants.Reset}");
        sb.AppendLine();
        sb.AppendLine();
    }

    private static void RenderOverview(StringBuilder sb, StatsData data) {
        sb.Append(TerminalColors.Muted);
        sb.Append("  Sessions: ");
        sb.Append(AnsiStyleEnumConstants.Reset);
        sb.AppendLine($"{data.TotalSessions}");

        sb.Append(TerminalColors.Muted);
        sb.Append("  Total Tokens: ");
        sb.Append(AnsiStyleEnumConstants.Reset);
        sb.AppendLine($"{NumberFormatter.FormatCompact(data.TotalTokens)}");

        sb.Append(TerminalColors.Muted);
        sb.Append("  Input Tokens: ");
        sb.Append(AnsiStyleEnumConstants.Reset);
        sb.AppendLine($"{NumberFormatter.FormatCompact(data.TotalInputTokens)}");

        sb.Append(TerminalColors.Muted);
        sb.Append("  Output Tokens: ");
        sb.Append(AnsiStyleEnumConstants.Reset);
        sb.AppendLine($"{NumberFormatter.FormatCompact(data.TotalOutputTokens)}");

        sb.Append(TerminalColors.Muted);
        sb.Append("  Total Cost: ");
        sb.Append(AnsiStyleEnumConstants.Reset);
        sb.AppendLine($"${data.TotalCostUsd:F2}");

        sb.Append(TerminalColors.Muted);
        sb.Append("  Active Days: ");
        sb.Append(AnsiStyleEnumConstants.Reset);
        sb.AppendLine($"{data.ActiveDays}");

        sb.Append(TerminalColors.Muted);
        sb.Append("  Longest Session: ");
        sb.Append(AnsiStyleEnumConstants.Reset);
        sb.AppendLine($"{data.LongestSessionMinutes}m");

        if (data.DailyUsage.Count > 0) {
            sb.AppendLine();
            RenderSparkline(sb, data.DailyUsage);
        }
    }

    private static void RenderModels(StringBuilder sb, StatsData data) {
        if (data.ModelBreakdown.Count == 0) {
            sb.Append($"  {AnsiStyleEnumConstants.Dim}No model data available{AnsiStyleEnumConstants.Reset}");
            sb.AppendLine();
            return;
        }

        RenderModelTable(sb, data);
    }

    private static void RenderDaily(StringBuilder sb, StatsData data) {
        if (data.DailyUsage.Count == 0) {
            sb.Append($"  {AnsiStyleEnumConstants.Dim}No daily usage data available{AnsiStyleEnumConstants.Reset}");
            sb.AppendLine();
            return;
        }

        var maxTokens = data.DailyUsage.Max(d => d.TotalTokens);
        if (maxTokens == 0) maxTokens = 1;

        foreach (var day in data.DailyUsage) {
            var barWidth = 25;
            var filled = (int)Math.Round((double)day.TotalTokens / maxTokens * barWidth);
            var bar = new string('█', filled) + new string('░', barWidth - filled);
            var color = day.TotalTokens > maxTokens * 0.8 ? TerminalColors.Warning : TerminalColors.Primary;

            sb.Append($"  {TerminalColors.Muted}{day.Date:MM/dd}{AnsiStyleEnumConstants.Reset} ");
            sb.Append($"{color}{bar}{AnsiStyleEnumConstants.Reset} ");
            sb.Append($"{NumberFormatter.FormatCompact(day.TotalTokens)}");
            sb.Append($" {TerminalColors.Muted}${day.CostUsd:F2}{AnsiStyleEnumConstants.Reset}");
            sb.AppendLine();
        }
    }

    private static void RenderSparkline(StringBuilder sb, List<DailyUsage> daily) {
        sb.AppendLine($"{AnsiStyleEnumConstants.Bold}Last 14 days{AnsiStyleEnumConstants.Reset}");
        sb.AppendLine();

        var recent = daily.TakeLast(14).ToList();
        if (recent.Count == 0) return;

        var maxTokens = recent.Max(d => d.TotalTokens);
        if (maxTokens == 0) maxTokens = 1;

        var blocks = new[] { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

        sb.Append("  ");
        foreach (var day in recent) {
            var idx = (int)Math.Round((double)day.TotalTokens / maxTokens * (blocks.Length - 1));
            if (idx < 0) idx = 0;
            if (idx >= blocks.Length) idx = blocks.Length - 1;
            sb.Append($"{TerminalColors.Primary}{blocks[idx]}{AnsiStyleEnumConstants.Reset}");
        }
        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}{recent[0].Date:MM/dd}{"",30}{recent[^1].Date:MM/dd}{AnsiStyleEnumConstants.Reset}");
        sb.AppendLine();
    }

    private static void RenderModelTable(StringBuilder sb, StatsData data) {
        var models = data.ModelBreakdown;
        var totalCost = models.Sum(m => m.CostUsd);

        var modelWidth = Math.Max(6, models.Max(m => m.Model.Length)) + 2;
        const int inputWidth = 10;
        const int outputWidth = 10;
        const int costWidth = 10;
        const int pctWidth = 8;

        var separator = new string('─', modelWidth + inputWidth + outputWidth + costWidth + pctWidth + 4);

        sb.AppendLine($"  {TerminalColors.Muted}{separator}{AnsiStyleEnumConstants.Reset}");

        var headerModel = "Model".PadRight(modelWidth);
        var headerInput = "Input".PadLeft(inputWidth);
        var headerOutput = "Output".PadLeft(outputWidth);
        var headerCost = "Cost".PadLeft(costWidth);
        var headerPct = "%".PadLeft(pctWidth);
        sb.AppendLine($"  {TerminalColors.Muted}{headerModel}{headerInput}{headerOutput}{headerCost}{headerPct}{AnsiStyleEnumConstants.Reset}");

        sb.AppendLine($"  {TerminalColors.Muted}{separator}{AnsiStyleEnumConstants.Reset}");

        foreach (var model in models) {
            var inputFmt = NumberFormatter.FormatCompact(model.InputTokens);
            var outputFmt = NumberFormatter.FormatCompact(model.OutputTokens);
            var costFmt = $"${model.CostUsd:F2}";
            var pct = totalCost > 0 ? (double)model.CostUsd / (double)totalCost * 100 : 0;
            var pctFmt = $"{pct:F1}%";

            var modelCol = model.Model.PadRight(modelWidth);
            var inputCol = inputFmt.PadLeft(inputWidth);
            var outputCol = outputFmt.PadLeft(outputWidth);
            var costCol = costFmt.PadLeft(costWidth);
            var pctCol = pctFmt.PadLeft(pctWidth);

            sb.Append(TerminalColors.Primary);
            sb.Append($"  {modelCol}");
            sb.Append(AnsiStyleEnumConstants.Reset);
            sb.Append($"{inputCol}{outputCol}{costCol}{pctCol}");
            sb.AppendLine();

            RenderCostBar(sb, pct);
        }

        sb.AppendLine($"  {TerminalColors.Muted}{separator}{AnsiStyleEnumConstants.Reset}");
    }

    private static void RenderCostBar(StringBuilder sb, double percentage) {
        const int barWidth = 30;
        var filled = (int)Math.Round(percentage / 100 * barWidth);
        if (filled < 0) filled = 0;
        if (filled > barWidth) filled = barWidth;
        var bar = new string('█', filled) + new string('░', barWidth - filled);
        sb.AppendLine($"  {TerminalColors.Accent}{bar}{AnsiStyleEnumConstants.Reset}");
    }
}