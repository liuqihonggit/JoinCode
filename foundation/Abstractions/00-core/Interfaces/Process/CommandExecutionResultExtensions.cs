namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// ICommandExecutionResult 扩展方法 — 统一结构化输出
/// <para>
/// ToMarkdownTable() 将命令执行结果渲染为 Markdown 表格,供 AI/用户消费
/// ToString() 由各实现类自行重写为简洁单行摘要(用于日志/调试)
/// </para>
/// </summary>
public static class CommandExecutionResultExtensions
{
    /// <summary>
    /// 将命令执行结果渲染为 Markdown 表格 — 包含退出码/成功状态/执行时长/输出摘要/错误摘要
    /// </summary>
    public static string ToMarkdownTable(this ICommandExecutionResult result)
    {
        var builder = new MarkdownTableBuilder()
            .WithTitle("命令执行结果")
            .AddHeader("字段", "值")
            .AddRow("退出码", (result.ExitCode?.ToString() ?? "null"))
            .AddRow("成功", result.Success ? "是" : "否")
            .AddRow("执行时长", FormatDuration(result.ExecutionTime))
            .AddRow("输出摘要", Summarize(result.Output))
            .AddRow("错误摘要", Summarize(result.Error));

        return builder.Build();
    }

    /// <summary>
    /// 将命令执行结果渲染为简洁 Markdown 表格 — 仅包含元数据(不含输出/错误正文),适合嵌入日志
    /// </summary>
    public static string ToMarkdownSummary(this ICommandExecutionResult result)
    {
        return new MarkdownTableBuilder()
            .AddHeader("ExitCode", "Success", "Duration")
            .AddRow(
                result.ExitCode?.ToString() ?? "null",
                result.Success ? "OK" : "FAIL",
                FormatDuration(result.ExecutionTime))
            .Build();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var ms = duration.TotalMilliseconds;
        return ms switch
        {
            0 => "0ms",
            < 1 => $"{ms:F2}ms",
            < 1000 => $"{ms:F0}ms",
            < 60000 => $"{duration.TotalSeconds:F2}s",
            _ => $"{duration.TotalMinutes:F2}min",
        };
    }

    private static string Summarize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "(空)";
        var trimmed = text.Trim();
        if (trimmed.Length <= 80) return trimmed;
        return string.Concat(trimmed.AsSpan(0, 77), "...");
    }
}
