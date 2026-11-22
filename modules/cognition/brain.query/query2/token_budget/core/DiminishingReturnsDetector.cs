namespace Core.Query.BudgetAnalysis;

/// <summary>
/// 递减回报检测器接口 — 基于 Token 消耗序列检测迭代效率递减
/// </summary>
public interface IDiminishingReturnsDetector {
    /// <summary>
    /// 检测最近的 Token 消耗序列是否存在递减回报（无状态：连续计数由调用方按查询上下文持有）
    /// </summary>
    /// <param name="recentConsumptions">最近的 Token 消耗记录</param>
    /// <param name="consecutiveLowValueCount">本次调用前已累计的连续低效次数</param>
    /// <returns>检测结果（ConsecutiveLowValueIterations 为本次调用后的累计值）</returns>
    DiminishingReturnsResult CheckDiminishingReturns(IReadOnlyList<TokenConsumption> recentConsumptions, int consecutiveLowValueCount);
}

/// <summary>
/// 递减回报检测结果
/// </summary>
public sealed class DiminishingReturnsResult {
    /// <summary>
    /// 是否处于递减回报状态
    /// </summary>
    public bool IsDiminishing { get; init; }

    /// <summary>
    /// 效率比率 — Token 增长率的平均值
    /// </summary>
    public double EffectivenessRatio { get; init; }

    /// <summary>
    /// 建议（可选）
    /// </summary>
    public string? Recommendation { get; init; }

    /// <summary>
    /// 连续低效迭代次数
    /// </summary>
    public int ConsecutiveLowValueIterations { get; init; }
}

/// <summary>
/// 递减回报检测器实现 — 基于 Token 增长率连续低效计数判定。
/// 无状态：IQueryEngine/中间件均为 Singleton，fork 子代理可并发查询；若把连续计数放在检测器字段上，
/// 一条查询的 Reset 会清掉另一条的计数，造成跨查询污染。计数改由每次调用显式传入/返回。
/// </summary>
[Register(typeof(IDiminishingReturnsDetector), ServiceLifetime.Singleton)]
public sealed partial class DiminishingReturnsDetector : ServiceEntity, IDiminishingReturnsDetector {
    private const double LowValueThreshold = 0.1;
    private const int ConsecutiveThreshold = 3;
    private const int MinimumSampleSize = 2;

    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造函数 — 注入遥测服务（可选）
    /// </summary>
    /// <param name="telemetryService">遥测服务</param>
    public DiminishingReturnsDetector(ITelemetryService? telemetryService = null) {
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// 检测最近的 Token 消耗序列是否存在递减回报
    /// </summary>
    /// <param name="recentConsumptions">最近的 Token 消耗记录</param>
    /// <param name="consecutiveLowValueCount">本次调用前已累计的连续低效次数</param>
    /// <returns>检测结果</returns>
    public DiminishingReturnsResult CheckDiminishingReturns(IReadOnlyList<TokenConsumption> recentConsumptions, int consecutiveLowValueCount) {
        ArgumentNullException.ThrowIfNull(recentConsumptions);

        if (recentConsumptions.Count < MinimumSampleSize) {
            return new DiminishingReturnsResult {
                IsDiminishing = false,
                EffectivenessRatio = 1.0,
                ConsecutiveLowValueIterations = consecutiveLowValueCount
            };
        }

        var ratios = new List<double>(recentConsumptions.Count - 1);
        for (var i = 1; i < recentConsumptions.Count; i++) {
            var prevConsumption = recentConsumptions[i - 1];
            var currConsumption = recentConsumptions[i];

            if (prevConsumption.Amount <= 0) {
                continue;
            }

            var tokenGrowthRate = (double)currConsumption.Amount / prevConsumption.Amount;
            ratios.Add(tokenGrowthRate);
        }

        if (ratios.Count == 0) {
            return new DiminishingReturnsResult {
                IsDiminishing = false,
                EffectivenessRatio = 1.0,
                ConsecutiveLowValueIterations = consecutiveLowValueCount
            };
        }

        var averageRatio = ratios.Average();
        var consecutiveCount = averageRatio < LowValueThreshold ? consecutiveLowValueCount + 1 : 0;

        var isDiminishing = consecutiveCount >= ConsecutiveThreshold;
        var recommendation = isDiminishing switch {
            true when consecutiveCount >= ConsecutiveThreshold + 2 => "Stop iteration - sustained diminishing returns",
            true => "Consider compacting context or switching strategy",
            _ => null
        };

        _telemetryService?.RecordCount("query.diminishing.check.count", new() { ["diminishing"] = isDiminishing.ToString() }, "count", "Diminishing returns check count");
        _telemetryService?.RecordHistogram("query.diminishing.effectiveness", averageRatio, unit: "ratio", description: "Effectiveness ratio");

        return new DiminishingReturnsResult {
            IsDiminishing = isDiminishing,
            EffectivenessRatio = averageRatio,
            Recommendation = recommendation,
            ConsecutiveLowValueIterations = consecutiveCount
        };
    }

    /// <inheritdoc />
    public override void Dispose() {
        base.Dispose();
    }
}