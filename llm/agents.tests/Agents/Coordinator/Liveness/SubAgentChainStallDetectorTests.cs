namespace Sync.Tests.Agents.Coordinator.Liveness;

/// <summary>
/// SubAgentChainStallDetector 单元测试 — 验证链路构建 + 全卡死判定（ADR 0106 L2）
/// </summary>
public sealed class SubAgentChainStallDetectorTests {
    [Fact]
    public void CheckChain_SingleConfirmedNode_BelowThreshold_NotStalled() {
        var detector = new SubAgentChainStallDetector(chainStallThreshold: 3);
        var parentMap = new Dictionary<string, string>();
        var confirmed = new HashSet<string> { "agent-1" };

        var result = detector.CheckChain("agent-1", parentMap, confirmed);

        result.TotalNodes.Should().Be(1);
        result.ConfirmedNodes.Should().Be(1);
        result.IsChainStalled.Should().BeFalse(); // 1 < 3 阈值
    }

    [Fact]
    public void CheckChain_AllConfirmed_AboveThreshold_IsStalled() {
        var detector = new SubAgentChainStallDetector(chainStallThreshold: 3);
        var parentMap = new Dictionary<string, string> {
            ["agent-3"] = "agent-2",
            ["agent-2"] = "agent-1",
        };
        var confirmed = new HashSet<string> { "agent-1", "agent-2", "agent-3" };

        var result = detector.CheckChain("agent-3", parentMap, confirmed);

        result.TotalNodes.Should().Be(3);
        result.ConfirmedNodes.Should().Be(3);
        result.IsChainStalled.Should().BeTrue();
    }

    [Fact]
    public void CheckChain_PartialConfirmed_NotStalled() {
        var detector = new SubAgentChainStallDetector(chainStallThreshold: 3);
        var parentMap = new Dictionary<string, string> {
            ["agent-3"] = "agent-2",
            ["agent-2"] = "agent-1",
        };
        var confirmed = new HashSet<string> { "agent-1", "agent-3" }; // agent-2 未确认

        var result = detector.CheckChain("agent-3", parentMap, confirmed);

        result.TotalNodes.Should().Be(3);
        result.ConfirmedNodes.Should().Be(2);
        result.IsChainStalled.Should().BeFalse();
    }

    [Fact]
    public void CheckChain_EmptyParentMap_SingleNodeChain() {
        var detector = new SubAgentChainStallDetector(chainStallThreshold: 1);
        var parentMap = new Dictionary<string, string>();
        var confirmed = new HashSet<string> { "agent-1" };

        var result = detector.CheckChain("agent-1", parentMap, confirmed);

        result.Chain.Should().ContainSingle().Which.Should().Be("agent-1");
        result.IsChainStalled.Should().BeTrue();
    }

    [Fact]
    public void CheckAllChains_ReturnsOnlyStalledChains() {
        var detector = new SubAgentChainStallDetector(chainStallThreshold: 2);
        var parentMap = new Dictionary<string, string> {
            ["agent-2"] = "agent-1",
            ["agent-4"] = "agent-3",
        };
        // agent-1+agent-2 全确认（2节点≥2阈值），agent-3+agent-4 只有 agent-4 确认
        var confirmed = new HashSet<string> { "agent-1", "agent-2", "agent-4" };

        var results = detector.CheckAllChains(confirmed, parentMap);

        results.Should().HaveCount(1);
        results[0].IsChainStalled.Should().BeTrue();
        results[0].TotalNodes.Should().Be(2);
    }

    [Fact]
    public void CheckChain_SelfLoopProtection_DoesNotInfiniteLoop() {
        var detector = new SubAgentChainStallDetector(chainStallThreshold: 1);
        var parentMap = new Dictionary<string, string> { ["agent-1"] = "agent-1" }; // 自环
        var confirmed = new HashSet<string> { "agent-1" };

        var result = detector.CheckChain("agent-1", parentMap, confirmed);

        result.TotalNodes.Should().Be(1); // 自环被保护，不无限循环
    }
}