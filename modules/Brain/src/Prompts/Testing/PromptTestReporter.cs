
namespace Core.Prompts.Testing;

/// <summary>
/// 提示词测试报告器
/// </summary>
public sealed partial class PromptTestReporter
{
    [Inject] private readonly ILogger<PromptTestReporter>? _logger;
    private readonly IFileSystem _fs;

    public PromptTestReporter(IFileSystem fs, ILogger<PromptTestReporter>? logger = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
    }
    
    /// <summary>
    /// 输出报告到日志
    /// </summary>
    public void WriteToLog(PromptTriggerReport report)
    {
        var reportContent = GenerateConsoleReport(report);
        _logger?.LogInformation("{Report}", reportContent);
    }

    /// <summary>
    /// 输出报告到控制台
    /// </summary>
    public void WriteToConsole(PromptTriggerReport report)
    {
        var reportContent = GenerateConsoleReport(report);
        _logger?.LogInformation("{Report}", reportContent);
    }

    /// <summary>
    /// 保存Markdown报告到文件
    /// </summary>
    public async Task SaveMarkdownReportAsync(PromptTriggerReport report, string filePath, CancellationToken cancellationToken = default)
    {
        var content = GenerateMarkdownReport(report);
        await _fs.WriteAllTextAsync(filePath, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 生成控制台报告
    /// </summary>
    public string GenerateConsoleReport(PromptTriggerReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("========================================");
        sb.AppendLine("   JoinCode 提示词触发测试报告");
        sb.AppendLine("========================================");
        sb.AppendLine();

        // 测试摘要
        sb.AppendLine("----------------------------------------");
        sb.AppendLine("测试摘要");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine($"总测试数: {report.TotalCount}");
        sb.AppendLine($"通过: {report.CorrectCount}");
        sb.AppendLine($"失败: {report.IncorrectCount}");
        sb.AppendLine($"触发: {report.TriggeredCount}");
        sb.AppendLine($"未触发: {report.NotTriggeredCount}");
        sb.AppendLine($"总耗时: {report.TotalDuration.TotalMilliseconds:F1}ms");
        sb.AppendLine();

        // 按场景分组显示
        var resultsByScenario = report.GetResultsByScenario();
        foreach (var (scenarioName, results) in resultsByScenario)
        {
            sb.AppendLine("----------------------------------------");
            sb.AppendLine($"场景: {scenarioName}");
            sb.AppendLine("----------------------------------------");
            sb.AppendLine();

            // 触发的Section
            var triggered = results.Where(r => r.IsTriggered).ToList();
            if (triggered.Count > 0)
            {
                sb.AppendLine($"触发的Section ({triggered.Count}):");
                foreach (var result in triggered)
                {
                    var status = result.IsCorrect ? StatusSymbol.Tick.ToValue() : StatusSymbol.Cross.ToValue();
                    var desc = result.ConditionDescription ?? "未知条件";
                    sb.AppendLine($"  {status} {result.SectionName,-30} [{desc}] ({result.Duration.TotalMilliseconds:F1}ms)");
                }
                sb.AppendLine();
            }

            // 未触发的Section
            var notTriggered = results.Where(r => !r.IsTriggered).ToList();
            if (notTriggered.Count > 0)
            {
                sb.AppendLine($"未触发的Section ({notTriggered.Count}):");
                foreach (var result in notTriggered)
                {
                    var status = result.IsCorrect ? StatusSymbol.Tick.ToValue() : StatusSymbol.Cross.ToValue();
                    var desc = result.ConditionDescription ?? "未知条件";
                    sb.AppendLine($"  {status} {result.SectionName,-30} [{desc}]");
                }
                sb.AppendLine();
            }
        }

        // 失败的测试详情
        var failedResults = report.GetFailedResults();
        if (failedResults.Count > 0)
        {
            sb.AppendLine("----------------------------------------");
            sb.AppendLine("失败的测试详情");
            sb.AppendLine("----------------------------------------");
            sb.AppendLine();

            foreach (var result in failedResults)
            {
                sb.AppendLine($"Section: {result.SectionName}");
                sb.AppendLine($"场景: {result.ScenarioName}");
                sb.AppendLine($"实际: {(result.IsTriggered ? "触发" : "未触发")}");
                sb.AppendLine($"预期: {(result.ExpectedTriggered ? "触发" : "未触发")}");
                sb.AppendLine($"条件: {result.ConditionDescription}");
                sb.AppendLine();
            }
        }

        // 最终结论
        sb.AppendLine("========================================");
        if (report.IncorrectCount == 0)
        {
            sb.AppendLine($"{StatusSymbol.Tick.ToValue()} 所有Section触发逻辑正确！");
        }
        else
        {
            sb.AppendLine($"{StatusSymbol.Cross.ToValue()} 发现 {report.IncorrectCount} 个触发逻辑错误");
        }
        sb.AppendLine("========================================");

        return sb.ToString();
    }

    /// <summary>
    /// 生成Markdown报告
    /// </summary>
    public string GenerateMarkdownReport(PromptTriggerReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# JoinCode 提示词触发测试报告");
        sb.AppendLine();
        sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // 摘要表格
        sb.AppendLine("## 测试摘要");
        sb.AppendLine();
        sb.AppendLine("| 指标 | 数值 |");
        sb.AppendLine("|------|------|");
        sb.AppendLine($"| 总测试数 | {report.TotalCount} |");
        sb.AppendLine($"| 通过 | {report.CorrectCount} |");
        sb.AppendLine($"| 失败 | {report.IncorrectCount} |");
        sb.AppendLine($"| 触发 | {report.TriggeredCount} |");
        sb.AppendLine($"| 未触发 | {report.NotTriggeredCount} |");
        sb.AppendLine($"| 总耗时 | {report.TotalDuration.TotalMilliseconds:F1}ms |");
        sb.AppendLine();

        // 按场景分组
        sb.AppendLine("## 按场景详细结果");
        sb.AppendLine();

        var resultsByScenario = report.GetResultsByScenario();
        foreach (var (scenarioName, results) in resultsByScenario)
        {
            sb.AppendLine($"### {scenarioName}");
            sb.AppendLine();
            sb.AppendLine("| Section | 状态 | 条件 | 耗时 |");
            sb.AppendLine("|---------|------|------|------|");

            foreach (var result in results)
            {
                var status = result.IsCorrect
                    ? (result.IsTriggered ? $"{StatusSymbol.Tick.ToValue()} 触发" : $"{StatusSymbol.Cross.ToValue()} 未触发")
                    : $"{StatusSymbol.Cross.ToValue()} 错误";
                var condition = result.ConditionDescription ?? "-";
                var duration = $"{result.Duration.TotalMilliseconds:F1}ms";

                sb.AppendLine($"| {result.SectionName} | {status} | {condition} | {duration} |");
            }

            sb.AppendLine();
        }

        // 失败的测试
        var failedResults = report.GetFailedResults();
        if (failedResults.Count > 0)
        {
            sb.AppendLine("## 失败的测试");
            sb.AppendLine();

            foreach (var result in failedResults)
            {
                sb.AppendLine($"### {result.SectionName} ({result.ScenarioName})");
                sb.AppendLine();
                sb.AppendLine($"- **实际**: {(result.IsTriggered ? "触发" : "未触发")}");
                sb.AppendLine($"- **预期**: {(result.ExpectedTriggered ? "触发" : "未触发")}");
                sb.AppendLine($"- **条件**: {result.ConditionDescription}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

}
