
namespace Core.Goal.Tests;

using JoinCode.Abstractions.Interfaces.Scheduling;
using JoinCode.Abstractions.Models.Goal;

public sealed class GoalNodeInspectorTests
{
    private readonly GoalNodeInspector _sut = new();

    private static GoalNodePayload CreateNode(string name, GoalNodeStatus status = GoalNodeStatus.Running, DateTime? startedAt = null, int loopIteration = 0)
    {
        return new GoalNodePayload
        {
            Kind = GoalNodeKind.Function,
            Name = name,
            Status = status,
            StartedAt = startedAt,
        };
    }

    [Fact]
    public async Task CheckHealthAsync_EmptyNodes_Should_Return_Healthy()
    {
        var report = await _sut.CheckHealthAsync([]).ConfigureAwait(true);
        Assert.False(report.HasAlerts);
    }

    [Fact]
    public async Task CheckHealthAsync_NodeTimeout_Should_Alert()
    {
        var now = DateTime.UtcNow;
        var node = CreateNode("slow_node", startedAt: now.AddMinutes(-31));

        var report = await _sut.CheckHealthAsync([node]).ConfigureAwait(true);

        Assert.True(report.HasAlerts);
        Assert.Contains(report.Alerts, a => a.Kind == NodeAlertKind.NodeTimeout && a.NodeId == "slow_node");
    }

    [Fact]
    public async Task CheckHealthAsync_NodeWithinTimeout_Should_NoAlert()
    {
        var now = DateTime.UtcNow;
        var node = CreateNode("ok_node", startedAt: now.AddMinutes(-10));

        var report = await _sut.CheckHealthAsync([node]).ConfigureAwait(true);

        Assert.False(report.HasAlerts);
    }

    [Fact]
    public async Task CheckHealthAsync_DeadLoop_Should_Alert()
    {
        var now = DateTime.UtcNow;
        var node = CreateNode("loop_node", startedAt: now.AddMinutes(-3));
        node.LoopIteration = 15;

        var report = await _sut.CheckHealthAsync([node]).ConfigureAwait(true);

        Assert.True(report.HasAlerts);
        Assert.Contains(report.Alerts, a => a.Kind == NodeAlertKind.DeadLoop && a.NodeId == "loop_node");
    }

    [Fact]
    public async Task CheckHealthAsync_HighIterationButLongTime_Should_NoDeadLoopAlert()
    {
        var now = DateTime.UtcNow;
        var node = CreateNode("slow_loop_node", startedAt: now.AddMinutes(-10));
        node.LoopIteration = 15;

        var report = await _sut.CheckHealthAsync([node]).ConfigureAwait(true);

        Assert.DoesNotContain(report.Alerts, a => a.Kind == NodeAlertKind.DeadLoop);
    }

    [Fact]
    public async Task CheckHealthAsync_FileConflict_Should_Alert()
    {
        var now = DateTime.UtcNow;
        var nodeA = CreateNode("node_a", startedAt: now);
        var nodeB = CreateNode("node_b", startedAt: now);
        var modifiedFiles = new Dictionary<string, IReadOnlyList<string>>
        {
            ["node_a"] = ["shared.cs"],
            ["node_b"] = ["shared.cs"],
        };

        var report = await _sut.CheckHealthAsync([nodeA, nodeB], modifiedFiles).ConfigureAwait(true);

        Assert.True(report.HasAlerts);
        Assert.Contains(report.Alerts, a => a.Kind == NodeAlertKind.FileConflict);
    }

    [Fact]
    public async Task CheckHealthAsync_NoFileConflict_Should_NoAlert()
    {
        var now = DateTime.UtcNow;
        var nodeA = CreateNode("node_a", startedAt: now);
        var nodeB = CreateNode("node_b", startedAt: now);
        var modifiedFiles = new Dictionary<string, IReadOnlyList<string>>
        {
            ["node_a"] = ["a.cs"],
            ["node_b"] = ["b.cs"],
        };

        var report = await _sut.CheckHealthAsync([nodeA, nodeB], modifiedFiles).ConfigureAwait(true);

        Assert.DoesNotContain(report.Alerts, a => a.Kind == NodeAlertKind.FileConflict);
    }

    [Fact]
    public async Task ObserveLoopAsync_FirstObservation_Should_NotTerminate()
    {
        var ctx = new LoopObservationContext
        {
            GoalId = "g1",
            NodeId = "neg_review",
            LoopIteration = 1,
            NegativeReviewCount = 5,
            TotalTokensConsumed = 100,
            TotalTurnsCompleted = 1,
        };

        var result = await _sut.ObserveLoopAsync(ctx).ConfigureAwait(true);
        Assert.False(result);
    }

    [Fact]
    public async Task ObserveLoopAsync_TrendImprovement_Should_Terminate()
    {
        var goalId = "g1";

        for (var i = 0; i < 2; i++)
        {
            await _sut.ObserveLoopAsync(new LoopObservationContext
            {
                GoalId = goalId,
                NodeId = "neg_review",
                LoopIteration = i + 1,
                NegativeReviewCount = 10,
                TotalTokensConsumed = 100,
                TotalTurnsCompleted = i + 1,
            }).ConfigureAwait(true);
        }

        var result = await _sut.ObserveLoopAsync(new LoopObservationContext
        {
            GoalId = goalId,
            NodeId = "neg_review",
            LoopIteration = 3,
            NegativeReviewCount = 5,
            TotalTokensConsumed = 100,
            TotalTurnsCompleted = 3,
        }).ConfigureAwait(true);

        Assert.True(result);
    }

    [Fact]
    public async Task ObserveLoopAsync_Stalemate_Should_Terminate()
    {
        var goalId = "g1";

        await _sut.ObserveLoopAsync(new LoopObservationContext
        {
            GoalId = goalId,
            NodeId = "neg_review",
            LoopIteration = 1,
            NegativeReviewCount = 5,
            TotalTokensConsumed = 100,
            TotalTurnsCompleted = 1,
        }).ConfigureAwait(true);

        await _sut.ObserveLoopAsync(new LoopObservationContext
        {
            GoalId = goalId,
            NodeId = "neg_review",
            LoopIteration = 2,
            NegativeReviewCount = 5,
            TotalTokensConsumed = 100,
            TotalTurnsCompleted = 2,
        }).ConfigureAwait(true);

        var result = await _sut.ObserveLoopAsync(new LoopObservationContext
        {
            GoalId = goalId,
            NodeId = "neg_review",
            LoopIteration = 3,
            NegativeReviewCount = 5,
            TotalTokensConsumed = 100,
            TotalTurnsCompleted = 3,
        }).ConfigureAwait(true);

        Assert.True(result);
    }

    [Fact]
    public async Task ObserveLoopAsync_NearHardLimit_Should_Terminate()
    {
        var goalId = "g1";

        await _sut.ObserveLoopAsync(new LoopObservationContext
        {
            GoalId = goalId,
            NodeId = "neg_review",
            LoopIteration = 1,
            NegativeReviewCount = 10,
            TotalTokensConsumed = 100,
            TotalTurnsCompleted = 1,
        }).ConfigureAwait(true);

        var result = await _sut.ObserveLoopAsync(new LoopObservationContext
        {
            GoalId = goalId,
            NodeId = "neg_review",
            LoopIteration = 12,
            NegativeReviewCount = 8,
            TotalTokensConsumed = 100,
            TotalTurnsCompleted = 12,
        }).ConfigureAwait(true);

        Assert.True(result);
    }

    [Fact]
    public async Task ScoreAsync_Should_Return_DefaultScore()
    {
        var score = await _sut.ScoreAsync("test output").ConfigureAwait(true);
        Assert.Equal(0.5, score.Overall);
    }

    [Fact]
    public async Task ScoreAsync_NullOutput_Should_Throw()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ScoreAsync("")).ConfigureAwait(true);
    }
}
