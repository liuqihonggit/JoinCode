namespace JoinCode.Composition.Tests.Ai;

/// <summary>
/// 诚实提示里的「目标复述」—— 对齐系统提示词 <c>goal_driven</c> 段最后一条：
/// 切模型时先复述已确认的目标，再列清单里未完成项。
/// <para>这里锁的是三条界线：引擎没就绪、状态加载失败、确实没有目标。
/// 前两条**不得**输出「（无）」—— 把「查不到」说成「没有」是假话，
/// 而模型会据此认定「用户没定过目标」，接着按自己的推断开工。</para>
/// </summary>
public sealed class HonestyNoticeTests {
    [Fact]
    public void RenderGoal_ShowsObjectiveAndProgress() {
        var state = new GoalState {
            Objective = "把配置键收敛成唯一清单",
            Status = GoalStatus.Pursuing,
            TurnsCompleted = 3,
            Constraints = ["不改协议原生 tools 字段"]
        };

        var lines = HonestyNotice.RenderGoal(state, engineAvailable: true);

        Assert.Equal("已确认的目标：把配置键收敛成唯一清单", lines[0]);
        Assert.Contains("已进行 3 轮", lines[1]);
        Assert.Contains(lines, line => line.Contains("不改协议原生 tools 字段"));
    }

    [Fact]
    public void RenderGoal_IncludesLastEvaluation() {
        var state = new GoalState {
            Objective = "跑通真机",
            LastEvaluation = GoalEvaluationResult.NotCompleted("还差 GUI 路径")
        };

        var lines = HonestyNotice.RenderGoal(state, engineAvailable: true);

        Assert.Contains(lines, line => line.Contains("上次评估 未达成 — 还差 GUI 路径"));
    }

    [Fact]
    public void RenderGoal_NoGoalStatesItPlainly() {
        var lines = HonestyNotice.RenderGoal(null, engineAvailable: true);

        Assert.Contains("（无）", lines[0]);
        Assert.Contains(lines, line => line.Contains("请先说明这次要做什么"));
    }

    [Fact]
    public void RenderGoal_EngineMissingNeverClaimsNoGoal() {
        var lines = HonestyNotice.RenderGoal(null, engineAvailable: false);

        Assert.Contains("无法核实", lines[0]);
        Assert.DoesNotContain(lines, line => line.Contains("（无）"));
    }

    [Fact]
    public void RenderGoal_LoadFailureNeverClaimsNoGoal() {
        var lines = HonestyNotice.RenderGoal(null, engineAvailable: true, loadError: "磁盘不可读");

        Assert.Contains("无法核实", lines[0]);
        Assert.Contains("磁盘不可读", lines[0]);
        Assert.DoesNotContain(lines, line => line.Contains("（无）"));
    }
}
