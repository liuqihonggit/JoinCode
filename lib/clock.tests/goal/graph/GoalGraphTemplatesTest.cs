namespace Core.Goal.Tests;

/// <summary>
/// GoalGraphTemplates 拆分出的 internal static 子方法确定性测试 — 纯计算,不依赖时序。
/// </summary>
public sealed class GoalGraphTemplatesTest {
    [Fact]
    public void BuildNegReviewSystemPrompt_Should_Contain_Evaluation_Checklist() {
        var prompt = GoalGraphTemplates.BuildNegReviewSystemPrompt();

        Assert.Contains("负向评价专家", prompt);
        Assert.Contains("评价清单", prompt);
        Assert.Contains("代码负向评价", prompt);
        Assert.Contains("功能遗留检查", prompt);
        Assert.Contains("更优做法搜索", prompt);
    }

    [Fact]
    public void BuildNegReviewSystemPrompt_Should_Contain_Route_Rules() {
        var prompt = GoalGraphTemplates.BuildNegReviewSystemPrompt();

        Assert.Contains("路由规则", prompt);
        Assert.Contains("NEG_STOP", prompt);
        Assert.Contains("NEG_CONTINUE", prompt);
        Assert.Contains("≤ 5", prompt);
        Assert.Contains("> 10", prompt);
    }

    [Fact]
    public void BuildNegReviewSystemPrompt_Should_Contain_Output_Format() {
        var prompt = GoalGraphTemplates.BuildNegReviewSystemPrompt();

        Assert.Contains("输出格式", prompt);
        Assert.Contains("```json", prompt);
        Assert.Contains("negativeReviewCount", prompt);
        Assert.Contains("route", prompt);
        Assert.Contains("items", prompt);
    }

    [Fact]
    public void BuildNegReviewSystemPrompt_Should_Be_Deterministic() {
        var prompt1 = GoalGraphTemplates.BuildNegReviewSystemPrompt();
        var prompt2 = GoalGraphTemplates.BuildNegReviewSystemPrompt();

        Assert.Equal(prompt1, prompt2);
    }

    [Fact]
    public void BuildNegReviewInstruction_Should_Contain_Objective() {
        var instruction = GoalGraphTemplates.BuildNegReviewInstruction("重构订单模块");

        Assert.Contains("重构订单模块", instruction);
        Assert.Contains("原始任务", instruction);
    }

    [Fact]
    public void BuildNegReviewInstruction_DifferentObjectives_Should_Produce_Different_Instructions() {
        var i1 = GoalGraphTemplates.BuildNegReviewInstruction("任务A");
        var i2 = GoalGraphTemplates.BuildNegReviewInstruction("任务B");

        Assert.NotEqual(i1, i2);
    }

    [Fact]
    public void BuildFixNegSystemPrompt_Should_Contain_Fix_Principles() {
        var prompt = GoalGraphTemplates.BuildFixNegSystemPrompt();

        Assert.Contains("修复专家", prompt);
        Assert.Contains("修复原则", prompt);
        Assert.Contains("task_update", prompt);
    }

    [Fact]
    public void BuildFixNegSystemPrompt_Should_Contain_Loop_Control() {
        var prompt = GoalGraphTemplates.BuildFixNegSystemPrompt();

        Assert.Contains("循环控制", prompt);
        Assert.Contains("NEG_CONTINUE", prompt);
        Assert.Contains("NEG_STOP", prompt);
        Assert.Contains("超过10条", prompt);
    }

    [Fact]
    public void BuildFixNegSystemPrompt_Should_Contain_Output_Format() {
        var prompt = GoalGraphTemplates.BuildFixNegSystemPrompt();

        Assert.Contains("输出格式", prompt);
        Assert.Contains("```json", prompt);
        Assert.Contains("fixedCount", prompt);
        Assert.Contains("remainingCount", prompt);
    }

    [Fact]
    public void BuildClusterAnalyzerSystemPrompt_Should_Contain_Roles() {
        var prompt = GoalGraphTemplates.BuildClusterAnalyzerSystemPrompt();

        Assert.Contains("cluster analysis agent", prompt);
        Assert.Contains("decomposability_analyzer", prompt);
        Assert.Contains("cluster_plan_validator", prompt);
        Assert.Contains("cluster_plan_approval", prompt);
    }

    [Fact]
    public void BuildClusterAnalyzerSystemPrompt_Should_Contain_Output_States() {
        var prompt = GoalGraphTemplates.BuildClusterAnalyzerSystemPrompt();

        Assert.Contains("NOT_DECOMPOSABLE", prompt);
        Assert.Contains("DECOMPOSABLE", prompt);
        Assert.Contains("BLOCKED", prompt);
    }

    [Fact]
    public void BuildClusterMergerSystemPrompt_Should_Contain_Evaluate_And_Synthesize() {
        var prompt = GoalGraphTemplates.BuildClusterMergerSystemPrompt();

        Assert.Contains("merge coordinator", prompt);
        Assert.Contains("EVALUATE", prompt);
        Assert.Contains("SYNTHESIZE", prompt);
    }

    [Fact]
    public void BuildClusterMergerSystemPrompt_Should_Contain_Json_Format() {
        var prompt = GoalGraphTemplates.BuildClusterMergerSystemPrompt();

        Assert.Contains("```json", prompt);
        Assert.Contains("worker_scores", prompt);
        Assert.Contains("merged_result", prompt);
        Assert.Contains("discarded_workers", prompt);
    }

    [Fact]
    public void BuildClusterMergerSystemPrompt_Should_Contain_Discard_Threshold() {
        var prompt = GoalGraphTemplates.BuildClusterMergerSystemPrompt();

        Assert.Contains("0.6", prompt);
        Assert.Contains("discard", prompt);
    }

    [Fact]
    public void BuildWorkerId_Should_Prefix_With_Worker_Underscore() {
        var id = GoalGraphTemplates.BuildWorkerId("sub_1");

        Assert.Equal("worker_sub_1", id);
    }

    [Fact]
    public void BuildWorkerId_Empty_Should_Return_Bare_Prefix() {
        var id = GoalGraphTemplates.BuildWorkerId("");

        Assert.Equal("worker_", id);
    }

    [Fact]
    public void BuildWorkerNodePayload_CodeVariant_Should_Map_To_Code() {
        var task = new SubTaskDefinition {
            Id = "sub_1",
            Title = "实现A",
            Description = "实现模块A",
            OwnedFiles = ["a.cs"],
            Variant = ExecutorVariant.Code,
        };

        var payload = GoalGraphTemplates.BuildWorkerNodePayload(task);

        Assert.Equal(GoalNodeKind.Agent, payload.Kind);
        Assert.Equal("worker-sub_1", payload.Name);
        Assert.Equal(AgentRole.Executor, payload.Role);
        Assert.Equal(ExecutorVariant.Code, payload.Variant);
        Assert.Equal(AgentIsolationMode.Worktree, payload.IsolationMode);
        Assert.Equal(2, payload.MaxLoopIterations);
        Assert.Equal("实现模块A", payload.Instruction);
    }

    [Fact]
    public void BuildWorkerNodePayload_ExploreVariant_Should_Map_To_Explore() {
        var task = new SubTaskDefinition {
            Id = "sub_2",
            Title = "调研B",
            Description = "调研方案B",
            OwnedFiles = [],
            Variant = ExecutorVariant.Explore,
        };

        var payload = GoalGraphTemplates.BuildWorkerNodePayload(task);

        Assert.Equal(ExecutorVariant.Explore, payload.Variant);
        Assert.Equal("worker-sub_2", payload.Name);
    }

    [Fact]
    public void BuildWorkerNodePayload_SystemPrompt_Should_Contain_Title_And_Files() {
        var task = new SubTaskDefinition {
            Id = "sub_1",
            Title = "实现认证",
            Description = "D",
            OwnedFiles = ["auth.cs", "token.cs"],
            Variant = ExecutorVariant.Code,
        };

        var payload = GoalGraphTemplates.BuildWorkerNodePayload(task);

        Assert.Contains("实现认证", payload.SystemPrompt);
        Assert.Contains("auth.cs", payload.SystemPrompt);
        Assert.Contains("token.cs", payload.SystemPrompt);
        Assert.Contains("Focus ONLY", payload.SystemPrompt);
    }

    [Fact]
    public void BuildWorkerNodePayload_EmptyOwnedFiles_Should_Contain_Empty_Files_List() {
        var task = new SubTaskDefinition {
            Id = "sub_1",
            Title = "T",
            Description = "D",
            OwnedFiles = [],
            Variant = ExecutorVariant.Code,
        };

        var payload = GoalGraphTemplates.BuildWorkerNodePayload(task);

        Assert.Contains("Files you own: ", payload.SystemPrompt);
    }

    [Fact]
    public void BuildNegReviewExecutePayload_Should_Set_Objective_As_Instruction() {
        var payload = GoalGraphTemplates.BuildNegReviewExecutePayload("实现用户注册");

        Assert.Equal(GoalNodeKind.Agent, payload.Kind);
        Assert.Equal(AgentRole.Executor.ToValue(), payload.Name);
        Assert.Equal(AgentRole.Executor, payload.Role);
        Assert.Equal(ExecutorVariant.Code, payload.Variant);
        Assert.Equal("实现用户注册", payload.Instruction);
        Assert.Contains("code execution expert", payload.SystemPrompt);
    }

    [Fact]
    public void BuildNegReviewExecutePayload_DifferentObjectives_Should_Produce_Different_Instructions() {
        var p1 = GoalGraphTemplates.BuildNegReviewExecutePayload("任务A");
        var p2 = GoalGraphTemplates.BuildNegReviewExecutePayload("任务B");

        Assert.NotEqual(p1.Instruction, p2.Instruction);
    }

    [Fact]
    public void BuildNegReviewPayload_Should_Set_Coordinator_Role_And_FreshContext() {
        var payload = GoalGraphTemplates.BuildNegReviewPayload("目标");

        Assert.Equal(GoalNodeKind.Agent, payload.Kind);
        Assert.Equal("negative-reviewer", payload.Name);
        Assert.Equal(AgentRole.Coordinator, payload.Role);
        Assert.True(payload.FreshContext);
        Assert.Equal(16, payload.MaxLoopIterations);
        Assert.Equal(RouteMatchMode.ConditionalOnly, payload.RouteMatchMode);
    }

    [Fact]
    public void BuildNegReviewPayload_Instruction_Should_Contain_Objective() {
        var payload = GoalGraphTemplates.BuildNegReviewPayload("重构支付模块");

        Assert.Contains("重构支付模块", payload.Instruction);
        Assert.Contains("原始任务", payload.Instruction);
    }

    [Fact]
    public void BuildNegReviewPayload_SystemPrompt_Should_Be_NegReviewSystemPrompt() {
        var payload = GoalGraphTemplates.BuildNegReviewPayload("目标");
        var expectedPrompt = GoalGraphTemplates.BuildNegReviewSystemPrompt();

        Assert.Equal(expectedPrompt, payload.SystemPrompt);
    }

    [Fact]
    public void BuildFixNegPayload_Should_Set_Executor_Role_And_ConditionalRouting() {
        var payload = GoalGraphTemplates.BuildFixNegPayload();

        Assert.Equal(GoalNodeKind.Agent, payload.Kind);
        Assert.Equal("fix-negative-review", payload.Name);
        Assert.Equal(AgentRole.Executor, payload.Role);
        Assert.Equal(ExecutorVariant.Code, payload.Variant);
        Assert.Equal(RouteMatchMode.ConditionalOnly, payload.RouteMatchMode);
    }

    [Fact]
    public void BuildFixNegPayload_Instruction_Should_Contain_Route_Options() {
        var payload = GoalGraphTemplates.BuildFixNegPayload();

        Assert.Contains("NEG_CONTINUE", payload.Instruction);
        Assert.Contains("NEG_STOP", payload.Instruction);
        Assert.Contains("超过10条", payload.Instruction);
    }

    [Fact]
    public void BuildFixNegPayload_SystemPrompt_Should_Be_FixNegSystemPrompt() {
        var payload = GoalGraphTemplates.BuildFixNegPayload();
        var expectedPrompt = GoalGraphTemplates.BuildFixNegSystemPrompt();

        Assert.Equal(expectedPrompt, payload.SystemPrompt);
    }

    [Fact]
    public void BuildNegReviewDonePayload_Should_Be_Function_Kind() {
        var payload = GoalGraphTemplates.BuildNegReviewDonePayload();

        Assert.Equal(GoalNodeKind.Function, payload.Kind);
        Assert.Equal("loop-done", payload.Name);
        Assert.Equal("Negative review loop completed", payload.Instruction);
    }

    [Fact]
    public void BuildClusterAnalyzePayload_Should_Set_Coordinator_Role() {
        var payload = GoalGraphTemplates.BuildClusterAnalyzePayload("并行处理多个模块");

        Assert.Equal(GoalNodeKind.Agent, payload.Kind);
        Assert.Equal("cluster-analyzer", payload.Name);
        Assert.Equal(AgentRole.Coordinator, payload.Role);
        Assert.Contains("并行处理多个模块", payload.Instruction);
        Assert.Contains("decomposed into parallel subtasks", payload.Instruction);
    }

    [Fact]
    public void BuildClusterAnalyzePayload_SystemPrompt_Should_Be_ClusterAnalyzerSystemPrompt() {
        var payload = GoalGraphTemplates.BuildClusterAnalyzePayload("目标");
        var expectedPrompt = GoalGraphTemplates.BuildClusterAnalyzerSystemPrompt();

        Assert.Equal(expectedPrompt, payload.SystemPrompt);
    }

    [Fact]
    public void BuildClusterAnalyzePayload_DifferentObjectives_Should_Produce_Different_Instructions() {
        var p1 = GoalGraphTemplates.BuildClusterAnalyzePayload("目标A");
        var p2 = GoalGraphTemplates.BuildClusterAnalyzePayload("目标B");

        Assert.NotEqual(p1.Instruction, p2.Instruction);
    }

    [Fact]
    public void BuildClusterExpandPayload_Should_Be_Function_Kind() {
        var payload = GoalGraphTemplates.BuildClusterExpandPayload();

        Assert.Equal(GoalNodeKind.Function, payload.Kind);
        Assert.Equal("cluster-expander", payload.Name);
        Assert.Contains("Dynamically expand", payload.Instruction);
    }

    [Fact]
    public void BuildClusterReviewPayload_Should_Set_Coordinator_Role_And_FreshContext() {
        var payload = GoalGraphTemplates.BuildClusterReviewPayload();

        Assert.Equal(GoalNodeKind.Agent, payload.Kind);
        Assert.Equal("cluster-reviewer", payload.Name);
        Assert.Equal(AgentRole.Coordinator, payload.Role);
        Assert.True(payload.FreshContext);
        Assert.Contains("independent reviewer", payload.SystemPrompt);
        Assert.Contains("Review the cluster execution results", payload.Instruction);
    }
}
