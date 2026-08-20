namespace Infra.Tests.HotSpot;

using Infrastructure.HotSpot;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Agent;

public sealed class WorktreeDecisionPolicyTests
{
    private readonly IWorktreeDecisionPolicy _sut = new WorktreeDecisionPolicy();

    [Theory]
    [InlineData(3, 0, 1, true, "TODO>=3")]
    [InlineData(5, 0, 1, true, "TODO=5")]
    [InlineData(2, 0, 1, false, "TODO<3无热文件低并行")]
    [InlineData(0, 1, 1, true, "涉及热文件>=1")]
    [InlineData(0, 0, 2, true, "并行度>=2")]
    [InlineData(1, 0, 1, false, "小任务单agent")]
    [InlineData(0, 0, 0, false, "空任务")]
    public void ShouldEnableWorktree_VariousInputs(int todo, int hot, int parallel, bool expected, string desc)
    {
        _sut.ShouldEnableWorktree(todo, hot, parallel).Should().Be(expected, desc);
    }

    [Fact]
    public void Decide_Disabled_ShouldAlwaysReturnNone()
    {
        _sut.Decide(false, ExecutorVariant.Code).Should().Be(AgentIsolationMode.None);
        _sut.Decide(false, ExecutorVariant.Explore).Should().Be(AgentIsolationMode.None);
    }

    [Theory]
    [InlineData(ExecutorVariant.Code, AgentIsolationMode.Worktree, "Code改代码开worktree")]
    [InlineData(ExecutorVariant.Verification, AgentIsolationMode.Worktree, "Verification验证改代码开worktree")]
    [InlineData(ExecutorVariant.Teammate, AgentIsolationMode.Worktree, "Teammate协作改代码开worktree")]
    [InlineData(ExecutorVariant.Explore, AgentIsolationMode.None, "Explore只读不开")]
    [InlineData(ExecutorVariant.Search, AgentIsolationMode.None, "Search只读不开")]
    [InlineData(ExecutorVariant.Plan, AgentIsolationMode.None, "Plan只读不开")]
    [InlineData(ExecutorVariant.Doctor, AgentIsolationMode.None, "Doctor后台不开")]
    public void Decide_Enabled_VariousVariants(ExecutorVariant variant, AgentIsolationMode expected, string desc)
    {
        _sut.Decide(true, variant).Should().Be(expected, desc);
    }

    [Fact]
    public void Decide_CustomThresholds_ShouldRespectConfig()
    {
        var sut = new WorktreeDecisionPolicy(todoThreshold: 10, hotFileThreshold: 5, parallelismThreshold: 5);

        sut.ShouldEnableWorktree(5, 0, 0).Should().BeFalse("TODO=5未达自定义阈值10");
        sut.ShouldEnableWorktree(10, 0, 0).Should().BeTrue("TODO=10达阈值");
    }
}
