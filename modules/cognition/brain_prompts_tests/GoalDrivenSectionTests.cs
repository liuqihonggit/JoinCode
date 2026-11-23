namespace Core.Tests.Prompts;

/// <summary>
/// 目标驱动提示词契约 — 锁住「先问目标、无目标不执行、推断要确认、模糊要讲利弊」四条硬规则。
/// <para>这些不是文案偏好：少一条，模型就会在目标未确认时开始改文件，
/// 或者把「推断」当成用户已经点头的目标。</para>
/// </summary>
public sealed class GoalDrivenSectionTests {
    [Fact]
    public void GetContent_RequiresAskingGoalBeforeAnyWork() {
        var content = GoalDrivenSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("这次的目标是什么？");
        content.Should().Contain("没有明确目标就不要执行任何任务");
    }

    [Fact]
    public void GetContent_TreatsInferredNeedsAsCandidatesOnly() {
        var content = GoalDrivenSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("逐条列出并让用户确认");
        content.Should().Contain("未确认的推断不得当成目标");
    }

    [Fact]
    public void GetContent_ForcesProsAndConsForVagueGoals() {
        var content = GoalDrivenSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("好处与代价");
        content.Should().Contain("2~3 个可判定的具体解释");
    }

    [Fact]
    public void GetContent_WiresGoalToolsAndModelSwitchReconfirmation() {
        var content = GoalDrivenSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("goal_get");
        content.Should().Contain("goal_graph_define");
        content.Should().Contain("goal_update");
        content.Should().Contain("切换模型");
        content.Should().Contain("重新确认方向");
    }
}
