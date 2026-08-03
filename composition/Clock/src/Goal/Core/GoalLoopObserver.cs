namespace Core.Goal;

using JoinCode.Abstractions.Interfaces;

/// <summary>
/// 基于规则的循环观察器 — 协调者窥探机制
/// 终止条件:
/// 1. 连续2轮负评条数递减 ≥ 30% → 趋势向好，终止循环
/// 2. 负评条数连续3轮相同 → 陷入僵局，终止循环
/// 3. 循环迭代 ≥ 12 且负评 ≤ 8 → 接近硬上限但质量尚可，提前终止
/// </summary>
[Register(typeof(IGoalLoopObserver))]
public sealed partial class GoalLoopObserver : IGoalLoopObserver
{
    [Inject] private readonly ILogger<GoalLoopObserver>? _logger;

    private readonly List<int> _negCountHistory = [];

    public Task<bool> ObserveAsync(LoopObservationContext context, CancellationToken cancellationToken = default)
    {
        _negCountHistory.Add(context.NegativeReviewCount);

        if (_negCountHistory.Count < 2)
        {
            _logger?.LogDebug("[GoalLoopObserver] 首次观察，继续循环 (负评={NegCount}, 迭代={Iter})",
                context.NegativeReviewCount, context.LoopIteration);
            return Task.FromResult(false);
        }

        var shouldTerminate = CheckTrendImprovement() || CheckStalemate() || CheckNearHardLimit(context);

        if (shouldTerminate)
        {
            _logger?.LogInformation("[GoalLoopObserver] 协调者建议终止循环 (负评={NegCount}, 迭代={Iter}, 历史=[{History}])",
                context.NegativeReviewCount, context.LoopIteration, string.Join(",", _negCountHistory));
        }

        return Task.FromResult(shouldTerminate);
    }

    private bool CheckTrendImprovement()
    {
        if (_negCountHistory.Count < 3)
            return false;

        var recent = _negCountHistory[^2];
        var current = _negCountHistory[^1];

        if (recent <= 0)
            return false;

        var reduction = (double)(recent - current) / recent;
        if (reduction >= 0.3 && current < recent)
        {
            _logger?.LogInformation("[GoalLoopObserver] 趋势向好: 负评从 {Prev} 降至 {Curr} (降幅 {Pct:P0})",
                recent, current, reduction);
            return true;
        }

        return false;
    }

    private bool CheckStalemate()
    {
        if (_negCountHistory.Count < 3)
            return false;

        var last3 = _negCountHistory[^3..];
        if (last3.Distinct().Count() == 1 && last3[0] > 0)
        {
            _logger?.LogInformation("[GoalLoopObserver] 僵局检测: 连续3轮负评均为 {Count}", last3[0]);
            return true;
        }

        return false;
    }

    private bool CheckNearHardLimit(LoopObservationContext context)
    {
        if (context.LoopIteration >= 12 && context.NegativeReviewCount <= 8)
        {
            _logger?.LogInformation("[GoalLoopObserver] 接近硬上限: 迭代={Iter} ≥ 12, 负评={Neg} ≤ 8",
                context.LoopIteration, context.NegativeReviewCount);
            return true;
        }

        return false;
    }
}
