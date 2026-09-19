namespace Core.Query.BudgetAnalysis;

/// <summary>
/// 递减回报检测器接口 — 基于 Token 消耗序列检测迭代效率递减
/// </summary>
public interface IDiminishingReturnsDetector {
    /// <summary>
    /// 检测最近的 Token 消耗序列是否存在递减回报
    /// </summary>
    /// <param name="recentConsumptions">最近的 Token 消耗记录</param>
    /// <returns>检测结果</returns>
    DiminishingReturnsResult CheckDiminishingReturns(IReadOnlyList<TokenConsumption> recentConsumptions);

    /// <summary>
    /// 重置检测器状态 — 清零连续低效计数
    /// </summary>
    void Reset();
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
/// 递减回报检测器实现 — 基于 Token 增长率连续低效计数判定
/// </summary>
[Register(typeof(IDiminishingReturnsDetector), ServiceLifetime.Singleton)]
public sealed partial class DiminishingReturnsDetector : ServiceEntity, IDiminishingReturnsDetector {
    private const double LowValueThreshold = 0.1;
    private const int ConsecutiveThreshold = 3;
    private const int MinimumSampleSize = 2;

    private int _consecutiveLowValueCount;
    private readonly AsyncLock _resetLock = new("DiminishingReturnsDetector");
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
    /// <returns>检测结果</returns>
    public DiminishingReturnsResult CheckDiminishingReturns(IReadOnlyList<TokenConsumption> recentConsumptions) {
        ArgumentNullException.ThrowIfNull(recentConsumptions);

        if (recentConsumptions.Count < MinimumSampleSize) {
            return new DiminishingReturnsResult {
                IsDiminishing = false,
                EffectivenessRatio = 1.0,
                ConsecutiveLowValueIterations = 0
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
                ConsecutiveLowValueIterations = 0
            };
        }

        var averageRatio = ratios.Average();

        using (_resetLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_resetLock.Name}' 等待超时")) {
            if (averageRatio < LowValueThreshold) {
                _consecutiveLowValueCount++;
            } else {
                _consecutiveLowValueCount = 0;
            }

            var isDiminishing = _consecutiveLowValueCount >= ConsecutiveThreshold;
            string? recommendation = isDiminishing switch {
                true when _consecutiveLowValueCount >= ConsecutiveThreshold + 2 => "Stop iteration - sustained diminishing returns",
                true => "Consider compacting context or switching strategy",
                _ => null
            };

            _telemetryService?.RecordCount("query.diminishing.check.count", new() { ["diminishing"] = isDiminishing.ToString() }, "count", "Diminishing returns check count");
            _telemetryService?.RecordHistogram("query.diminishing.effectiveness", averageRatio, unit: "ratio", description: "Effectiveness ratio");

            return new DiminishingReturnsResult {
                IsDiminishing = isDiminishing,
                EffectivenessRatio = averageRatio,
                Recommendation = recommendation,
                ConsecutiveLowValueIterations = _consecutiveLowValueCount
            };
        }
    }

    /// <summary>
    /// 重置检测器状态 — 清零连续低效计数
    /// </summary>
    public void Reset() {
        using (_resetLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_resetLock.Name}' 等待超时")) {
            _consecutiveLowValueCount = 0;
        }
    }

    /// <inheritdoc />
    public override void Dispose() {
        _resetLock.Dispose();
        base.Dispose();
    }
}