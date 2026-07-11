
namespace Core.Tests.Scheduling;

/// <summary>
/// ToolPortingPlanRunner 单元测试类
/// 测试计划运行器的各种功能，包括 RunAsync 方法、GenerateAssignmentPlan 和导出功能
/// </summary>
public class ToolPortingPlanRunnerTests
{
    private ParallelExecutionEngine CreateSimulatedExecutionEngine()
    {
        return new ParallelExecutionEngine(simulationMode: true, NullLogger<ParallelExecutionEngine>.Instance);
    }

    #region GenerateAssignmentPlan 测试

    /// <summary>
    /// 测试 GenerateAssignmentPlan 应返回有效的任务分配计划
    /// </summary>
    [Fact]
    public void GenerateAssignmentPlan_ShouldReturnValidPlan()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var plan = runner.GenerateAssignmentPlan();

        plan.Should().NotBeNull();
        plan.TotalTasks.Should().BeGreaterThan(0);
        plan.Assignments.Should().NotBeNull();
        plan.Assignments.Should().NotBeEmpty();
    }

    /// <summary>
    /// 测试 GenerateAssignmentPlan 应正确计算第一波任务数量
    /// </summary>
    [Fact]
    public void GenerateAssignmentPlan_ShouldCalculateFirstWaveCount()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var plan = runner.GenerateAssignmentPlan();

        plan.FirstWaveCount.Should().BeGreaterThan(0);
        plan.FirstWaveCount.Should().Be(plan.Assignments.Count(a => a.IsFirstWave));
    }

    /// <summary>
    /// 测试 GenerateAssignmentPlan 应正确计算第二波任务数量
    /// </summary>
    [Fact]
    public void GenerateAssignmentPlan_ShouldCalculateSecondWaveCount()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var plan = runner.GenerateAssignmentPlan();

        plan.SecondWaveCount.Should().BeGreaterThanOrEqualTo(0);
        plan.SecondWaveCount.Should().Be(plan.Assignments.Count(a => !a.IsFirstWave));
    }

    /// <summary>
    /// 测试 GenerateAssignmentPlan 应正确计算所需 Agent 总数
    /// </summary>
    [Fact]
    public void GenerateAssignmentPlan_ShouldCalculateTotalAgentsRequired()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var plan = runner.GenerateAssignmentPlan();

        plan.TotalAgentsRequired.Should().BeGreaterThan(0);
        plan.TotalAgentsRequired.Should().Be(plan.Assignments.Sum(a => a.RequiredAgents));
    }

    /// <summary>
    /// 测试 GenerateAssignmentPlan 应包含执行顺序信息
    /// </summary>
    [Fact]
    public void GenerateAssignmentPlan_ShouldContainExecutionOrder()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var plan = runner.GenerateAssignmentPlan();

        plan.ExecutionOrder.Should().NotBeNull();
        plan.ExecutionOrder.Should().NotBeEmpty();
    }

    /// <summary>
    /// 测试任务分配应包含完整的任务信息
    /// </summary>
    [Fact]
    public void GenerateAssignmentPlan_AssignmentsShouldContainCompleteInfo()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var plan = runner.GenerateAssignmentPlan();
        var assignment = plan.Assignments.First();

        assignment.TaskId.Should().NotBeNullOrEmpty();
        assignment.TaskName.Should().NotBeNullOrEmpty();
        assignment.Description.Should().NotBeNullOrEmpty();
        assignment.RequiredAgents.Should().BeGreaterThan(0);
        assignment.AgentWorkScopes.Should().NotBeNull();
        assignment.Dependencies.Should().NotBeNull();
    }

    /// <summary>
    /// 测试第一波任务的依赖列表应为空
    /// </summary>
    [Fact]
    public void GenerateAssignmentPlan_FirstWaveTasksShouldHaveNoDependencies()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var plan = runner.GenerateAssignmentPlan();
        var firstWaveAssignments = plan.Assignments.Where(a => a.IsFirstWave);

        foreach (var assignment in firstWaveAssignments)
        {
            assignment.Dependencies.Should().BeEmpty();
        }
    }

    /// <summary>
    /// 测试第二波任务应包含依赖信息
    /// </summary>
    [Fact]
    public void GenerateAssignmentPlan_SecondWaveTasksShouldHaveDependencies()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var plan = runner.GenerateAssignmentPlan();
        var secondWaveAssignments = plan.Assignments.Where(a => !a.IsFirstWave);

        foreach (var assignment in secondWaveAssignments)
        {
            assignment.Dependencies.Should().NotBeEmpty();
        }
    }

    #endregion

    #region 导出功能测试 - JSON

    /// <summary>
    /// 测试 ExportPlanToJson 应返回有效的 JSON 字符串
    /// </summary>
    [Fact]
    public void ExportPlanToJson_ShouldReturnValidJson()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var json = runner.ExportPlanToJson();

        json.Should().NotBeNullOrEmpty();

        var exception = Record.Exception(() => JsonDocument.Parse(json));
        exception.Should().BeNull();
    }

    /// <summary>
    /// 测试导出的 JSON 应包含计划的关键字段
    /// </summary>
    [Fact]
    public void ExportPlanToJson_ShouldContainKeyFields()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var json = runner.ExportPlanToJson();

        json.Should().Contain("totalTasks");
        json.Should().Contain("firstWaveCount");
        json.Should().Contain("secondWaveCount");
        json.Should().Contain("totalAgentsRequired");
        json.Should().Contain("assignments");
        json.Should().Contain("executionOrder");
    }

    /// <summary>
    /// 测试导出的 JSON 应使用 camelCase 命名策略
    /// </summary>
    [Fact]
    public void ExportPlanToJson_ShouldUseCamelCaseNaming()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var json = runner.ExportPlanToJson();

        json.Should().Contain("totalTasks");
        json.Should().NotContain("TotalTasks");
    }

    /// <summary>
    /// 测试导出的 JSON 应格式化为缩进形式
    /// </summary>
    [Fact]
    public void ExportPlanToJson_ShouldBeIndented()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var json = runner.ExportPlanToJson();

        json.Should().Contain("\n");
        json.Should().Contain("  ");
    }

    /// <summary>
    /// 测试导出的 JSON 可以被反序列化
    /// </summary>
    [Fact]
    public void ExportPlanToJson_ShouldBeDeserializable()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var json = runner.ExportPlanToJson();

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var plan = JsonSerializer.Deserialize<TaskAssignmentPlan>(json, options);

        plan.Should().NotBeNull();
        plan!.TotalTasks.Should().BeGreaterThan(0);
        plan.Assignments.Should().NotBeEmpty();
    }

    #endregion

    #region 导出功能测试 - Markdown

    /// <summary>
    /// 测试 ExportPlanToMarkdown 应返回有效的 Markdown 字符串
    /// </summary>
    [Fact]
    public void ExportPlanToMarkdown_ShouldReturnValidMarkdown()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var markdown = runner.ExportPlanToMarkdown();

        markdown.Should().NotBeNullOrEmpty();
        markdown.Should().Contain(L.T(StringKey.PortingPlanTitle));
    }

    /// <summary>
    /// 测试导出的 Markdown 应包含概览部分
    /// </summary>
    [Fact]
    public void ExportPlanToMarkdown_ShouldContainOverviewSection()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var markdown = runner.ExportPlanToMarkdown();

        markdown.Should().Contain(L.T(StringKey.SectionOverview));
        // TotalTasksLabel/FirstWaveLabel/SecondWaveLabel/SuggestedAgentsLabel 是带 {0} 的格式化字符串
        // markdown 中已是格式化后的值（如 "- **总任务数**: 12"），断言用前缀匹配
        markdown.Should().Contain(L.T(StringKey.TotalTasksLabel).Split('{')[0]);
        markdown.Should().Contain(L.T(StringKey.FirstWaveLabel).Split('{')[0]);
        markdown.Should().Contain(L.T(StringKey.SecondWaveLabel).Split('{')[0]);
        markdown.Should().Contain(L.T(StringKey.SuggestedAgentsLabel).Split('{')[0]);
    }

    /// <summary>
    /// 测试导出的 Markdown 应包含第一波任务部分
    /// </summary>
    [Fact]
    public void ExportPlanToMarkdown_ShouldContainFirstWaveSection()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var markdown = runner.ExportPlanToMarkdown();

        markdown.Should().Contain(L.T(StringKey.FirstWaveImmediateStart));
        markdown.Should().Contain(L.T(StringKey.FirstWaveTableHeader));
    }

    /// <summary>
    /// 测试导出的 Markdown 应包含第二波任务部分
    /// </summary>
    [Fact]
    public void ExportPlanToMarkdown_ShouldContainSecondWaveSection()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var markdown = runner.ExportPlanToMarkdown();

        markdown.Should().Contain(L.T(StringKey.SecondWaveConditionalTrigger));
        markdown.Should().Contain(L.T(StringKey.SecondWaveTableHeader));
    }

    /// <summary>
    /// 测试导出的 Markdown 应包含执行顺序部分
    /// </summary>
    [Fact]
    public void ExportPlanToMarkdown_ShouldContainExecutionOrderSection()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var markdown = runner.ExportPlanToMarkdown();

        markdown.Should().Contain(L.T(StringKey.SectionExecutionOrder));
    }

    /// <summary>
    /// 测试导出的 Markdown 应包含依赖关系图部分
    /// </summary>
    [Fact]
    public void ExportPlanToMarkdown_ShouldContainDependencyGraphSection()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var markdown = runner.ExportPlanToMarkdown();

        markdown.Should().Contain(L.T(StringKey.SectionDependencyGraph));
        markdown.Should().Contain(L.T(StringKey.FirstWaveParallelStart));
    }

    /// <summary>
    /// 测试导出的 Markdown 应包含优先级图标
    /// </summary>
    [Fact]
    public void ExportPlanToMarkdown_ShouldContainPriorityIcons()
    {
        var executionEngine = CreateSimulatedExecutionEngine();
        var runner = new ToolPortingPlanRunner(executionEngine, NullLogger<ToolPortingPlanRunner>.Instance);
        var markdown = runner.ExportPlanToMarkdown();

        markdown.Should().ContainAny(PrioritySymbolConstants.Critical, PrioritySymbolConstants.High, PrioritySymbolConstants.Medium, PrioritySymbolConstants.Low);
    }

    #endregion

    #region PlanOptions 测试

    /// <summary>
    /// 测试 PlanOptions 默认值
    /// </summary>
    [Fact]
    public void PlanOptions_DefaultValues_ShouldBeCorrect()
    {
        var options = new PlanOptions();

        options.SimulatedMode.Should().BeTrue();
        options.Verbose.Should().BeTrue();
    }

    /// <summary>
    /// 测试 PlanOptions 属性设置
    /// </summary>
    [Fact]
    public void PlanOptions_SetProperties_ShouldWorkCorrectly()
    {
        var options = new PlanOptions
        {
            SimulatedMode = false,
            Verbose = false
        };

        options.SimulatedMode.Should().BeFalse();
        options.Verbose.Should().BeFalse();
    }

    #endregion

    #region PlanExecutionResult 测试

    /// <summary>
    /// 测试 PlanExecutionResult 属性设置
    /// </summary>
    [Fact]
    public void PlanExecutionResult_SetProperties_ShouldWorkCorrectly()
    {
        var report = new ExecutionReport
        {
            TotalTasks = 10,
            CompletedTasks = new List<ScheduledTask>(),
            FailedTasks = new List<ScheduledTask>(),
            PendingTasks = new List<ScheduledTask>(),
            CompletionPercentage = 100.0,
            ExecutionDuration = TimeSpan.FromSeconds(30),
            TaskDetails = new List<TaskExecutionDetail>()
        };

        var result = new ToolPortingExecutionResult
        {
            Success = true,
            Report = report,
            ExecutionLog = "测试日志内容"
        };

        result.Success.Should().BeTrue();
        result.Report.Should().Be(report);
        result.ExecutionLog.Should().Be("测试日志内容");
    }

    #endregion

    #region TaskAssignmentPlan 测试

    /// <summary>
    /// 测试 TaskAssignmentPlan 属性
    /// </summary>
    [Fact]
    public void TaskAssignmentPlan_Properties_ShouldWorkCorrectly()
    {
        var assignments = new List<TaskAgentAssignment>
        {
            new()
            {
                TaskId = "task-001",
                TaskName = "测试任务",
                Description = "任务描述",
                RequiredAgents = 2,
                Priority = TodoPriority.High,
                Dependencies = new List<string>(),
                IsFirstWave = true,
                AgentWorkScopes = new List<string> { "范围1", "范围2" }
            }
        };

        var executionOrder = new List<ExecutionPhase>
        {
            new()
            {
                PhaseNumber = 1,
                Description = "第一阶段",
                TaskNames = new List<string> { "task-001" }
            }
        };

        var plan = new TaskAssignmentPlan
        {
            TotalTasks = 1,
            FirstWaveCount = 1,
            SecondWaveCount = 0,
            TotalAgentsRequired = 2,
            Assignments = assignments,
            ExecutionOrder = executionOrder
        };

        plan.TotalTasks.Should().Be(1);
        plan.FirstWaveCount.Should().Be(1);
        plan.SecondWaveCount.Should().Be(0);
        plan.TotalAgentsRequired.Should().Be(2);
        plan.Assignments.Should().BeEquivalentTo(assignments);
        plan.ExecutionOrder.Should().BeEquivalentTo(executionOrder);
    }

    #endregion

    #region TaskAgentAssignment 测试

    /// <summary>
    /// 测试 TaskAgentAssignment 属性
    /// </summary>
    [Fact]
    public void TaskAgentAssignment_Properties_ShouldWorkCorrectly()
    {
        var assignment = new TaskAgentAssignment
        {
            TaskId = "task-001",
            TaskName = "Agent 调度核心框架",
            Description = "调度器接口、状态管理、生命周期管理、内存快照",
            RequiredAgents = 2,
            Priority = TodoPriority.Critical,
            Dependencies = new List<string> { "dep-001" },
            IsFirstWave = false,
            AgentWorkScopes = new List<string> { "范围1", "范围2" }
        };

        assignment.TaskId.Should().Be("task-001");
        assignment.TaskName.Should().Be("Agent 调度核心框架");
        assignment.Description.Should().Be("调度器接口、状态管理、生命周期管理、内存快照");
        assignment.RequiredAgents.Should().Be(2);
        assignment.Priority.Should().Be(TodoPriority.Critical);
        assignment.Dependencies.Should().ContainSingle();
        assignment.IsFirstWave.Should().BeFalse();
        assignment.AgentWorkScopes.Should().HaveCount(2);
    }

    #endregion

    #region ExecutionPhase 测试

    /// <summary>
    /// 测试 ExecutionPhase 属性
    /// </summary>
    [Fact]
    public void ExecutionPhase_Properties_ShouldWorkCorrectly()
    {
        var phase = new ExecutionPhase
        {
            PhaseNumber = 1,
            Description = "第一波：9个独立任务并行启动",
            TaskNames = new List<string> { "Task-01", "Task-02", "Task-03" }
        };

        phase.PhaseNumber.Should().Be(1);
        phase.Description.Should().Be("第一波：9个独立任务并行启动");
        phase.TaskNames.Should().HaveCount(3);
    }

    #endregion
}
