namespace JoinCode.Cli;

/// <summary>
/// 统计渲染器 — 纯文本终端输出
/// </summary>
public sealed class StatsRenderer
{
    public string Render(StatsData data, StatsTab tab = StatsTab.Overview)
    {
        var sb = new StringBuilder();

        RenderTabHeader(sb, tab, data);

        switch (tab)
        {
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

    private static void RenderTabHeader(StringBuilder sb, StatsTab activeTab, StatsData data)
    {
        var range = "";
        if (data.DateRangeStart.HasValue && data.DateRangeEnd.HasValue)
        {
            range = $" ({data.DateRangeStart.Value:MMM d} - {data.DateRangeEnd.Value:MMM d})";
        }

        sb.AppendLine($"{AnsiStyleConstants.Bold}Stats{range}{AnsiStyleConstants.Reset}");
        sb.AppendLine();

        var tabs = new[] { ("Overview", StatsTab.Overview), ("Models", StatsTab.Models), ("Daily", StatsTab.Daily) };
        var tabParts = new List<string>();
        foreach (var (label, t) in tabs)
        {
            if (t == activeTab)
            {
                tabParts.Add($"{TerminalColors.Accent}{AnsiStyleConstants.Bold}{label}{AnsiStyleConstants.Reset}");
            }
            else
            {
                tabParts.Add($"{AnsiStyleConstants.Dim}{label}{AnsiStyleConstants.Reset}");
            }
        }
        sb.Append("  ");
        sb.AppendLine(string.Join($" {TerminalColors.Muted}│{AnsiStyleConstants.Reset} ", tabParts));
        sb.Append($"  {TerminalColors.Muted}{new string('─', 40)}{AnsiStyleConstants.Reset}");
        sb.AppendLine();
        sb.AppendLine();
    }

    private static void RenderOverview(StringBuilder sb, StatsData data)
    {
        sb.Append(TerminalColors.Muted);
        sb.Append("  Sessions: ");
        sb.Append(AnsiStyleConstants.Reset);
        sb.AppendLine($"{data.TotalSessions}");

        sb.Append(TerminalColors.Muted);
        sb.Append("  Total Tokens: ");
        sb.Append(AnsiStyleConstants.Reset);
        sb.AppendLine($"{NumberFormatter.FormatCompact(data.TotalTokens)}");

        sb.Append(TerminalColors.Muted);
        sb.Append("  Input Tokens: ");
        sb.Append(AnsiStyleConstants.Reset);
        sb.AppendLine($"{NumberFormatter.FormatCompact(data.TotalInputTokens)}");

        sb.Append(TerminalColors.Muted);
        sb.Append("  Output Tokens: ");
        sb.Append(AnsiStyleConstants.Reset);
        sb.AppendLine($"{NumberFormatter.FormatCompact(data.TotalOutputTokens)}");

        sb.Append(TerminalColors.Muted);
        sb.Append("  Total Cost: ");
        sb.Append(AnsiStyleConstants.Reset);
        sb.AppendLine($"${data.TotalCostUsd:F2}");

        sb.Append(TerminalColors.Muted);
        sb.Append("  Active Days: ");
        sb.Append(AnsiStyleConstants.Reset);
        sb.AppendLine($"{data.ActiveDays}");

        sb.Append(TerminalColors.Muted);
        sb.Append("  Longest Session: ");
        sb.Append(AnsiStyleConstants.Reset);
        sb.AppendLine($"{data.LongestSessionMinutes}m");

        if (data.DailyUsage.Count > 0)
        {
            sb.AppendLine();
            RenderSparkline(sb, data.DailyUsage);
        }
    }

    private static void RenderModels(StringBuilder sb, StatsData data)
    {
        if (data.ModelBreakdown.Count == 0)
        {
            sb.Append($"  {AnsiStyleConstants.Dim}No model data available{AnsiStyleConstants.Reset}");
            sb.AppendLine();
            return;
        }

        RenderModelTable(sb, data);
    }

    private static void RenderDaily(StringBuilder sb, StatsData data)
    {
        if (data.DailyUsage.Count == 0)
        {
            sb.Append($"  {AnsiStyleConstants.Dim}No daily usage data available{AnsiStyleConstants.Reset}");
            sb.AppendLine();
            return;
        }

        var maxTokens = data.DailyUsage.Max(d => d.TotalTokens);
        if (maxTokens == 0) maxTokens = 1;

        foreach (var day in data.DailyUsage)
        {
            var barWidth = 25;
            var filled = (int)Math.Round((double)day.TotalTokens / maxTokens * barWidth);
            var bar = new string('█', filled) + new string('░', barWidth - filled);
            var color = day.TotalTokens > maxTokens * 0.8 ? TerminalColors.Warning : TerminalColors.Primary;

            sb.Append($"  {TerminalColors.Muted}{day.Date:MM/dd}{AnsiStyleConstants.Reset} ");
            sb.Append($"{color}{bar}{AnsiStyleConstants.Reset} ");
            sb.Append($"{NumberFormatter.FormatCompact(day.TotalTokens)}");
            sb.Append($" {TerminalColors.Muted}${day.CostUsd:F2}{AnsiStyleConstants.Reset}");
            sb.AppendLine();
        }
    }

    private static void RenderSparkline(StringBuilder sb, List<DailyUsage> daily)
    {
        sb.AppendLine($"{AnsiStyleConstants.Bold}Last 14 days{AnsiStyleConstants.Reset}");
        sb.AppendLine();

        var recent = daily.TakeLast(14).ToList();
        if (recent.Count == 0) return;

        var maxTokens = recent.Max(d => d.TotalTokens);
        if (maxTokens == 0) maxTokens = 1;

        var blocks = new[] { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" };

        sb.Append("  ");
        foreach (var day in recent)
        {
            var idx = (int)Math.Round((double)day.TotalTokens / maxTokens * (blocks.Length - 1));
            if (idx < 0) idx = 0;
            if (idx >= blocks.Length) idx = blocks.Length - 1;
            sb.Append($"{TerminalColors.Primary}{blocks[idx]}{AnsiStyleConstants.Reset}");
        }
        sb.AppendLine();
        sb.Append($"  {TerminalColors.Muted}{recent[0].Date:MM/dd}{"",30}{recent[^1].Date:MM/dd}{AnsiStyleConstants.Reset}");
        sb.AppendLine();
    }

    private static void RenderModelTable(StringBuilder sb, StatsData data)
    {
        var models = data.ModelBreakdown;
        var totalCost = models.Sum(m => m.CostUsd);

        var modelWidth = Math.Max(6, models.Max(m => m.Model.Length)) + 2;
        const int inputWidth = 10;
        const int outputWidth = 10;
        const int costWidth = 10;
        const int pctWidth = 8;

        var separator = new string('─', modelWidth + inputWidth + outputWidth + costWidth + pctWidth + 4);

        sb.AppendLine($"  {TerminalColors.Muted}{separator}{AnsiStyleConstants.Reset}");

        var headerModel = "Model".PadRight(modelWidth);
        var headerInput = "Input".PadLeft(inputWidth);
        var headerOutput = "Output".PadLeft(outputWidth);
        var headerCost = "Cost".PadLeft(costWidth);
        var headerPct = "%".PadLeft(pctWidth);
        sb.AppendLine($"  {TerminalColors.Muted}{headerModel}{headerInput}{headerOutput}{headerCost}{headerPct}{AnsiStyleConstants.Reset}");

        sb.AppendLine($"  {TerminalColors.Muted}{separator}{AnsiStyleConstants.Reset}");

        foreach (var model in models)
        {
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
            sb.Append(AnsiStyleConstants.Reset);
            sb.Append($"{inputCol}{outputCol}{costCol}{pctCol}");
            sb.AppendLine();

            RenderCostBar(sb, pct);
        }

        sb.AppendLine($"  {TerminalColors.Muted}{separator}{AnsiStyleConstants.Reset}");
    }

    private static void RenderCostBar(StringBuilder sb, double percentage)
    {
        const int barWidth = 30;
        var filled = (int)Math.Round(percentage / 100 * barWidth);
        if (filled < 0) filled = 0;
        if (filled > barWidth) filled = barWidth;
        var bar = new string('█', filled) + new string('░', barWidth - filled);
        sb.AppendLine($"  {TerminalColors.Accent}{bar}{AnsiStyleConstants.Reset}");
    }
}
