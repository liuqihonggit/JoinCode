namespace Composition.Tests.Commands;

/// <summary>
/// /workflows 命令单元测试 — 覆盖空参+list(管理器缺失/空列表/已加载渲染)/run(缺名用法/
/// 执行器缺失/插件未找到/完整执行链)、status(缺 ID 用法/执行器缺失/正常状态快照)/未知操作
/// <para>IPluginManager 与 IWorkflowTaskExecutor 用 Moq 桩经本地构造 CommandServices 注入;
/// 完整执行链用真实 WorkflowPluginHost+mock IWorkflowPlugin 在 LoadAsync 中注册 WorkflowDefinition</para>
/// </summary>
[Collection("command-output")]
public sealed class WorkflowsCommandTest {
    private static Mock<IWorkflowTaskExecutor> CreateExecutor(WorkflowResult? result = null, WorkflowStatus? status = null) {
        var executor = new Mock<IWorkflowTaskExecutor>();
        executor.Setup(e => e.ExecuteWorkflowAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result ?? new WorkflowResult {
                WorkflowId = "wf-run-1",
                Status = TaskState.Completed,
                Duration = TimeSpan.FromMilliseconds(120),
            });
        executor.Setup(e => e.GetWorkflowStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status ?? new WorkflowStatus {
                WorkflowId = "wf-run-1",
                State = TaskState.Completed,
                CompletedSteps = 2,
                TotalSteps = 3,
            });
        return executor;
    }

    private static (WorkflowsCommand Command, Mock<IPluginManager> PluginManager, Mock<IWorkflowTaskExecutor> Executor, ChatCommandContext Context) Arrange(
        string arguments = "",
        Mock<IPluginManager>? pluginManager = null,
        bool withPluginManager = true,
        Mock<IWorkflowTaskExecutor>? executor = null,
        bool withExecutor = true,
        IReadOnlyList<string>? loadedWorkflows = null) {
        var manager = pluginManager ?? new Mock<IPluginManager>();
        manager.Setup(m => m.LoadedWorkflowPluginNames).Returns(loadedWorkflows ?? []);

        var effectiveExecutor = executor ?? CreateExecutor();

        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            PluginManager = withPluginManager ? manager.Object : null,
            WorkflowTaskExecutor = withExecutor ? effectiveExecutor.Object : null,
        };
        var context = CommandTestHarness.CreateContext(commandServices, arguments: arguments);
        return (new WorkflowsCommand(), manager, effectiveExecutor, context);
    }

    [Fact]
    public async Task Execute_EmptyArgs_WithoutManager_ShowsUninitialized() {
        var (cmd, _, _, context) = Arrange(arguments: "", withPluginManager: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("插件管理器未初始化");
    }

    [Fact]
    public async Task Execute_ListArg_WithLoadedWorkflows_RendersNames() {
        var (cmd, manager, _, context) = Arrange(arguments: "list", loadedWorkflows: ["deploy-wf", "test-wf"]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.LoadedWorkflowPluginNames, Times.AtLeastOnce);
        output.Should().Contain("deploy-wf");
        output.Should().Contain("test-wf");
        output.Should().Contain("/workflows run <name>");
    }

    [Fact]
    public async Task Execute_ListArg_NoLoadedWorkflows_ShowsEmptyHint() {
        var (cmd, _, _, context) = Arrange(arguments: "list", loadedWorkflows: []);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前无已加载的工作流");
    }

    [Fact]
    public async Task Execute_EmptyArgs_SameAsList() {
        var (cmd, _, _, context) = Arrange(arguments: "", loadedWorkflows: ["only-wf"]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("only-wf", "空参默认等价 list");
    }

    [Fact]
    public async Task Execute_Run_WithoutName_ShowsUsage() {
        var (cmd, _, executor, context) = Arrange(arguments: "run");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("/workflows run <name>");
        executor.Verify(e => e.ExecuteWorkflowAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()),
            Times.Never, "缺工作流名时不应执行");
    }

    [Fact]
    public async Task Execute_Run_WithoutExecutor_ShowsUninitialized() {
        var (cmd, _, executor, context) = Arrange(arguments: "run deploy-wf", withExecutor: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("工作流执行器未初始化");
        executor.Verify(e => e.ExecuteWorkflowAsync(It.IsAny<WorkflowDefinition>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_Run_UnknownPlugin_ShowsNotFound() {
        var manager = new Mock<IPluginManager>();
        manager.Setup(m => m.LoadedWorkflowPluginNames).Returns(["other-wf"]);
        manager.Setup(m => m.GetWorkflowPlugin(It.IsAny<string>())).Returns((WorkflowPluginHost?)null);
        var (cmd, managerMock, _, context) = Arrange(arguments: "run missing-wf", pluginManager: manager);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        managerMock.Verify(m => m.GetWorkflowPlugin("missing-wf"), Times.Once);
        output.Should().Contain("missing-wf", "插件未找到提示应含工作流名");
    }

    [Fact]
    public async Task Execute_Run_LoadedWorkflow_ExecutesAndRendersResult() {
        var definition = new WorkflowDefinition {
            WorkflowId = "deploy-wf",
            Steps = [new WorkflowStep { StepId = "step-1", Name = "构建" }],
        };
        var plugin = new Mock<IWorkflowPlugin>();
        plugin.Setup(p => p.Name).Returns("deploy-wf");
        plugin.Setup(p => p.Version).Returns("1.0");
        plugin.Setup(p => p.LoadAsync(It.IsAny<PluginContext>(), It.IsAny<CancellationToken>()))
            .Callback<PluginContext, CancellationToken>((ctx, _) =>
                ctx.ConfigureServices(s => s.AddSingleton(definition)))
            .ReturnsAsync(OperationResult.Ok());
        // JCC9108/9105: await using 声明由编译器展开 try-finally,测试结束自动释放插件容器;
        // 借用语义对齐 TASK065 断层9(命令侧不拥有宿主生命周期,测试侧作为构造方负责释放)
        await using var host = new WorkflowPluginHost(plugin.Object);
        await host.LoadAsync(CancellationToken.None);
        {
            var manager = new Mock<IPluginManager>();
            manager.Setup(m => m.LoadedWorkflowPluginNames).Returns(["deploy-wf"]);
            manager.Setup(m => m.GetWorkflowPlugin("deploy-wf")).Returns(host);
            var executor = CreateExecutor(result: new WorkflowResult {
                WorkflowId = "wf-run-1",
                Status = TaskState.Completed,
                Duration = TimeSpan.FromMilliseconds(120),
            });
            var (cmd, _, executorMock, context) = Arrange(
                arguments: "run deploy-wf", pluginManager: manager, executor: executor);

            var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

            result.ShouldContinue.Should().BeTrue();
            executorMock.Verify(e => e.ExecuteWorkflowAsync(
                It.Is<WorkflowDefinition>(d => d.WorkflowId == "deploy-wf"),
                It.IsAny<CancellationToken>()), Times.Once, "应执行插件注册的工作流定义");
            output.Should().Contain("deploy-wf");
            output.Should().Contain("wf-run-1");
            output.Should().Contain("120ms");
        }
    }

    [Fact]
    public async Task Execute_Status_WithoutId_ShowsUsage() {
        var (cmd, _, executor, context) = Arrange(arguments: "status");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("/workflows status <workflow_id>");
        executor.Verify(e => e.GetWorkflowStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "缺工作流 ID 时不应查询");
    }

    [Fact]
    public async Task Execute_Status_WithoutExecutor_ShowsUninitialized() {
        var (cmd, _, executor, context) = Arrange(arguments: "status wf-1", withExecutor: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("工作流执行器未初始化");
    }

    [Fact]
    public async Task Execute_Status_WithId_RendersStatusSnapshot() {
        var status = new WorkflowStatus {
            WorkflowId = "wf-query",
            State = TaskState.Running,
            CompletedSteps = 1,
            TotalSteps = 4,
            StepStatuses = new Dictionary<string, StepStatus> {
                ["step-1"] = new() { StepId = "step-1", State = StepState.Completed, Duration = TimeSpan.FromMilliseconds(50) },
                ["step-2"] = new() { StepId = "step-2", State = StepState.Running, Error = null },
            },
        };
        var executor = CreateExecutor(status: status);
        var (cmd, _, executorMock, context) = Arrange(arguments: "status wf-query", executor: executor);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        executorMock.Verify(e => e.GetWorkflowStatusAsync("wf-query", It.IsAny<CancellationToken>()), Times.Once);
        output.Should().Contain("wf-query");
        output.Should().Contain("Running");
        output.Should().Contain("1/4");
    }

    [Fact]
    public async Task Execute_UnknownAction_ShowsSupportedActions() {
        var (cmd, _, _, context) = Arrange(arguments: "bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("bogus", "未知操作提示应含操作名");
        output.Should().Contain("list, run, status");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new WorkflowsCommand();
        cmd.Name.Should().Be("workflows");
        cmd.Usage.Should().Be("/workflows [list|run|status] [name]");
    }
}
