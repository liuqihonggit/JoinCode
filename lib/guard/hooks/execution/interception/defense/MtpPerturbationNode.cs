namespace Core.Hooks.Execution.Interception.Defense;

/// <summary>
/// 扰动特征 — 从单次工具调用中提取的统计特征
/// </summary>
/// <param name="CommandLength">命令长度</param>
/// <param name="HasRedirectSymbol">是否包含重定向符号</param>
/// <param name="HasPathLikeToken">是否包含路径样 token</param>
/// <param name="Timestamp">记录时间</param>
public sealed record PerturbationFeatures(
    int CommandLength,
    bool HasRedirectSymbol,
    bool HasPathLikeToken,
    DateTimeOffset Timestamp);

/// <summary>
/// 扰动分析报告
/// </summary>
/// <param name="ShouldTriggerAdaptive">是否应触发自适应开关（连续异常达到阈值）</param>
/// <param name="ConsecutiveAnomalies">连续异常次数</param>
/// <param name="TotalRecords">总记录数</param>
/// <param name="LastCommand">最近一次命令</param>
public sealed record PerturbationReport(
    bool ShouldTriggerAdaptive,
    int ConsecutiveAnomalies,
    int TotalRecords,
    string? LastCommand);

/// <summary>
/// MTP 扰动检测 node — 独立公共对象，PostToolUse 扰动统计 + 自适应触发。
/// <para>
/// MTP 扰动纵深防御约束第6条：PostToolUse 只审计不清理（清理本身也是 bash 操作）。
/// 约束第7条：自适应开关基于统计特征，不依赖 typo 关键字。
/// </para>
/// <para>
/// 统计特征：
/// <list type="bullet">
/// <item>命令长度偏差 ±1~2 字符（MTP 丢字符/乱入字符）</item>
/// <item>重定向符号异常（&gt;/dev/null → &gt;nul 等）</item>
/// <item>路径拼写偏移（./build → ./buld 等）</item>
/// </list>
/// 连续 N 次偏差 → 触发自适应开关（自动启用 AntiCharLossConfirm 模式）。
/// </para>
/// </summary>
[Register(typeof(MtpPerturbationNode), ServiceLifetime.Singleton)]
public sealed class MtpPerturbationNode
{
    private const int MaxRecords = 100;
    private const int AnomalyThreshold = 3;
    private const int LengthDeviationThreshold = 2;

    private readonly ConcurrentQueue<PerturbationRecord> _recentRecords = new();
    private volatile bool _isAdaptiveTriggered;

    /// <summary>
    /// 自适应是否已触发 — 连续异常达到阈值后置 true，后续 bash 调用自动启用 AntiCharLossConfirm。
    /// <para>
    /// MTP 扰动纵深防御约束第7条：自适应开关基于统计特征，不依赖 typo 关键字。
    /// </para>
    /// </summary>
    public bool IsAdaptiveTriggered => _isAdaptiveTriggered;

    /// <summary>
    /// 重置自适应触发（手动恢复或配置变更时调用）。
    /// </summary>
    public void ResetAdaptiveTrigger() => _isAdaptiveTriggered = false;

    /// <summary>
    /// 记录一次工具调用，返回当前扰动分析报告。
    /// <para>
    /// 在 PostToolUse 阶段调用（执行后），只做统计不做拦截（约束第6条）。
    /// </para>
    /// </summary>
    /// <param name="command">执行的命令</param>
    /// <param name="exitCode">退出码（0=成功，非0=失败）</param>
    /// <param name="stderr">stderr 输出（用于检测异常）</param>
    /// <returns>扰动分析报告</returns>
    public PerturbationReport Record(string command, int exitCode, string? stderr)
    {
        var features = ExtractFeatures(command);
        var isAnomaly = DetectAnomaly(features, exitCode, stderr);
        var record = new PerturbationRecord(features, isAnomaly, command);

        _recentRecords.Enqueue(record);
        TrimQueue();

        var report = AnalyzeRecentRecords();
        if (report.ShouldTriggerAdaptive)
            _isAdaptiveTriggered = true;

        return report;
    }

    /// <summary>
    /// 分析最近记录，检测是否应触发自适应开关。
    /// </summary>
    public PerturbationReport AnalyzeRecentRecords()
    {
        var recent = _recentRecords.ToArray();
        var consecutiveAnomalies = CountConsecutiveAnomalies(recent);
        var lastCommand = recent.Length > 0 ? recent[^1].Command : null;

        return new PerturbationReport(
            ShouldTriggerAdaptive: consecutiveAnomalies >= AnomalyThreshold,
            ConsecutiveAnomalies: consecutiveAnomalies,
            TotalRecords: recent.Length,
            LastCommand: lastCommand);
    }

    /// <summary>
    /// 提取命令的扰动特征。
    /// </summary>
    private static PerturbationFeatures ExtractFeatures(string command)
        => new(
            CommandLength: command.Length,
            HasRedirectSymbol: command.Contains('>') || command.Contains('<'),
            HasPathLikeToken: command.Contains('/') || command.Contains('\\') || command.Contains('~'),
            Timestamp: DateTimeOffset.UtcNow);

    /// <summary>
    /// 检测单次调用是否为异常（扰动特征）。
    /// <para>
    /// 异常判定：
    /// <list type="bullet">
    /// <item>非零退出码 + 包含重定向符号 → 可能重定向目标错误</item>
    /// <item>stderr 包含 "not found" / "No such file" → 可能路径拼写偏移</item>
    /// </list>
    /// </para>
    /// </summary>
    private static bool DetectAnomaly(PerturbationFeatures features, int exitCode, string? stderr)
    {
        if (exitCode != 0 && features.HasRedirectSymbol)
            return true;

        if (stderr is not null && ContainsPathError(stderr))
            return true;

        return false;
    }

    /// <summary>
    /// 检测 stderr 是否包含路径错误（拼写偏移特征）。
    /// </summary>
    private static bool ContainsPathError(string stderr)
        => stderr.Contains("not found", StringComparison.OrdinalIgnoreCase)
           || stderr.Contains("No such file", StringComparison.OrdinalIgnoreCase)
           || stderr.Contains("cannot access", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 计算最近记录中连续异常的次数（从最新往前数）。
    /// </summary>
    private static int CountConsecutiveAnomalies(PerturbationRecord[] records)
    {
        var count = 0;
        for (var i = records.Length - 1; i >= 0; i--)
        {
            if (!records[i].IsAnomaly)
                break;
            count++;
        }
        return count;
    }

    /// <summary>
    /// 修剪队列到最大长度。
    /// </summary>
    private void TrimQueue()
    {
        while (_recentRecords.Count > MaxRecords)
            _recentRecords.TryDequeue(out _);
    }

    private sealed record PerturbationRecord(
        PerturbationFeatures Features,
        bool IsAnomaly,
        string Command);
}
