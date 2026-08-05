namespace Core.Goal;

using JoinCode.Abstractions.Interfaces.Scheduling;
using JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 目标节点检查器 — 统一实现健康检查 + 循环观察 + 质量评分。
/// 合并原 GoalLoopObserver（循环观察）+ GoalNodeHealthChecker（健康检查）+ GoalQualityScorer（评分）。
/// </summary>
[Register(typeof(IGoalNodeInspector))]
public sealed partial class GoalNodeInspector : ServiceEntity, IGoalNodeInspector
{
    private const int DeadLoopMaxIterations = 10;
    private static readonly TimeSpan NodeTimeoutThreshold = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DeadLoopTimeWindow = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, List<int>> _loopHistoryByGoal = new(StringComparer.Ordinal);

    [Inject] private readonly ILogger<GoalNodeInspector>? _logger;
    [Inject] private readonly IClockService _clock;

    public GoalNodeInspector(ILogger<GoalNodeInspector>? logger = null, IClockService? clock = null)
    {
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <inheritdoc />
    public Task<NodeHealthReport> CheckHealthAsync(
        IReadOnlyList<GoalNodePayload> activeNodes,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? nodeModifiedFiles = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeNodes);

        var alerts = new List<NodeHealthAlert>();
        var now = _clock.GetUtcNow();

        foreach (var node in activeNodes)
        {
            CheckNodeTimeout(node, now, alerts);
            CheckDeadLoop(node, now, alerts);
        }

        if (nodeModifiedFiles is not null)
        {
            CheckFileConflicts(activeNodes, nodeModifiedFiles, alerts);
        }

        var report = alerts.Count > 0
            ? NodeHealthReport.WithAlerts(alerts)
            : NodeHealthReport.Healthy();

        return Task.FromResult(report);
    }

    /// <inheritdoc />
    public Task<bool> ObserveLoopAsync(LoopObservationContext context, CancellationToken cancellationToken = default)
    {
        if (!_loopHistoryByGoal.TryGetValue(context.GoalId, out var history))
        {
            history = [];
            _loopHistoryByGoal[context.GoalId] = history;
        }

        history.Add(context.NegativeReviewCount);

        if (history.Count < 2)
        {
            _logger?.LogDebug("[GoalNodeInspector] 首次观察，继续循环 (Goal={GoalId}, 负评={NegCount}, 迭代={Iter})",
                context.GoalId, context.NegativeReviewCount, context.LoopIteration);
            return Task.FromResult(false);
        }

        var shouldTerminate = CheckTrendImprovement(history) || CheckStalemate(history) || CheckNearHardLimit(context);

        if (shouldTerminate)
        {
            _loopHistoryByGoal.Remove(context.GoalId);
            _logger?.LogInformation("[GoalNodeInspector] 建议终止循环 (Goal={GoalId}, 负评={NegCount}, 迭代={Iter}, 历史=[{History}])",
                context.GoalId, context.NegativeReviewCount, context.LoopIteration, string.Join(",", history));
        }

        return Task.FromResult(shouldTerminate);
    }

    /// <inheritdoc />
    public Task<NodeQualityScore> ScoreAsync(string nodeOutput, IReadOnlyList<string>? criteria = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeOutput);
        return Task.FromResult(NodeQualityScore.Default);
    }

    private void CheckNodeTimeout(GoalNodePayload node, DateTime now, List<NodeHealthAlert> alerts)
    {
        if (node.Status != GoalNodeStatus.Running || node.StartedAt is not { } startedAt)
            return;

        var elapsed = now - startedAt;
        if (elapsed > NodeTimeoutThreshold)
        {
            alerts.Add(new NodeHealthAlert
            {
                NodeId = node.Name,
                Kind = NodeAlertKind.NodeTimeout,
                Message = $"节点运行超时: 已运行 {elapsed.TotalMinutes:F0} 分钟 (阈值 {NodeTimeoutThreshold.TotalMinutes:F0} 分钟)",
            });
        }
    }

    private void CheckDeadLoop(GoalNodePayload node, DateTime now, List<NodeHealthAlert> alerts)
    {
        if (node.LoopIteration <= DeadLoopMaxIterations || node.StartedAt is not { } startedAt)
            return;

        var elapsed = now - startedAt;
        if (elapsed < DeadLoopTimeWindow)
        {
            alerts.Add(new NodeHealthAlert
            {
                NodeId = node.Name,
                Kind = NodeAlertKind.DeadLoop,
                Message = $"死循环检测: 迭代 {node.LoopIteration} 次耗时 {elapsed.TotalSeconds:F0} 秒 (阈值 {DeadLoopMaxIterations} 次/{DeadLoopTimeWindow.TotalMinutes:F0} 分钟)",
            });
        }
    }

    private static void CheckFileConflicts(
        IReadOnlyList<GoalNodePayload> activeNodes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> nodeModifiedFiles,
        List<NodeHealthAlert> alerts)
    {
        var runningNodes = activeNodes.Where(n => n.Status == GoalNodeStatus.Running).ToList();
        if (runningNodes.Count < 2)
            return;

        var fileToNodes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in runningNodes)
        {
            if (!nodeModifiedFiles.TryGetValue(node.Name, out var files))
                continue;

            foreach (var file in files)
            {
                if (!fileToNodes.TryGetValue(file, out var nodeList))
                {
                    nodeList = [];
                    fileToNodes[file] = nodeList;
                }
                nodeList.Add(node.Name);
            }
        }

        foreach (var (file, nodeIds) in fileToNodes)
        {
            if (nodeIds.Count >= 2)
            {
                alerts.Add(new NodeHealthAlert
                {
                    NodeId = string.Join(",", nodeIds),
                    Kind = NodeAlertKind.FileConflict,
                    Message = $"运行时文件冲突: 文件 '{file}' 被 {nodeIds.Count} 个运行中节点同时修改: {string.Join(", ", nodeIds)}",
                });
            }
        }
    }

    private bool CheckTrendImprovement(List<int> history)
    {
        if (history.Count < 3)
            return false;

        var recent = history[^2];
        var current = history[^1];

        if (recent <= 0)
            return false;

        var reduction = (double)(recent - current) / recent;
        if (reduction >= 0.3 && current < recent)
        {
            _logger?.LogInformation("[GoalNodeInspector] 趋势向好: 负评从 {Prev} 降至 {Curr} (降幅 {Pct:P0})",
                recent, current, reduction);
            return true;
        }

        return false;
    }

    private bool CheckStalemate(List<int> history)
    {
        if (history.Count < 3)
            return false;

        var last3 = history[^3..];
        if (last3.Distinct().Count() == 1 && last3[0] > 0)
        {
            _logger?.LogInformation("[GoalNodeInspector] 僵局检测: 连续3轮负评均为 {Count}", last3[0]);
            return true;
        }

        return false;
    }

    private bool CheckNearHardLimit(LoopObservationContext context)
    {
        if (context.LoopIteration >= 12 && context.NegativeReviewCount <= 8)
        {
            _logger?.LogInformation("[GoalNodeInspector] 接近硬上限: 迭代={Iter} ≥ 12, 负评={Neg} ≤ 8",
                context.LoopIteration, context.NegativeReviewCount);
            return true;
        }

        return false;
    }
}
