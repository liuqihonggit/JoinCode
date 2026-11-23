namespace Composition.Tests.Commands;

/// <summary>
/// /goal 命令单元测试 — 覆盖引擎缺失降级/空参(无目标启动 GoalSpec 收集/有目标显示状态)/
/// pause/resume/clear 生命周期(含别名族)/带初始提示与约束预算启动/StartAsync 异常收敛/
/// --cron 定时模式(注册任务/存储缺失/表达式缺失)/registry 优先路径(列表/收集/生命周期)/
/// ParseGoalArgs 参数解析纯函数/元数据一致性
/// <para>GoalEngine/GoalRegistry/CronTaskStore 用 Moq 桩经 CommandServices 可选字段注入</para>
/// </summary>
[Collection("command-output")]
public sealed class GoalCommandTest {
    /// <summary>剥离 ANSI 转义序列 — 目标状态渲染带颜色码,断言基于纯文本</summary>
    private static string StripAnsi(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\x1B\[[0-9;]*m", string.Empty);

    private static GoalState Goal(
        string id = "goal-1",
        string objective = "优化启动性能",
        GoalStatus status = GoalStatus.Pursuing) => new() {
        GoalId = id,
        Objective = objective,
        Status = status,
        TokensUsed = 500,
        TurnsCompleted = 3,
        TokenBudget = 1000,
        Constraints = ["不许改公共 API"],
        CreatedAt = DateTime.UtcNow.AddMinutes(-10),
    };

    /// <summary>构造 GoalEngine 桩 — StartAsync 返回运行中目标,生命周期方法空实现</summary>
    private static Mock<IGoalEngine> CreateEngine(GoalState? currentState = null) {
        var engine = new Mock<IGoalEngine>();
        engine.Setup(e => e.CurrentState).Returns(currentState);
        engine.Setup(e => e.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Goal());
        engine.Setup(e => e.PauseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        engine.Setup(e => e.ResumeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        engine.Setup(e => e.ClearAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        engine.Setup(e => e.RehydrateAsync(It.IsAny<CancellationToken>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        return engine;
    }

    private static (GoalCommand Command, ChatCommandContext Context) Arrange(
        string arguments = "",
        Mock<IGoalEngine>? engine = null,
        Mock<IGoalRegistry>? registry = null,
        Mock<ICronTaskStore>? cronStore = null) {
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            GoalEngine = engine?.Object,
            GoalRegistry = registry?.Object,
            CronTaskStore = cronStore?.Object,
        };
        var context = new ChatCommandContext {
            Arguments = arguments,
            SessionId = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };
        return (new GoalCommand(), context);
    }

    [Fact]
    public async Task Execute_WithoutEngineOrRegistry_ShowsError() {
        var (cmd, context) = Arrange(arguments: "--status");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("错误: 目标引擎未注册", "引擎与注册表均缺失应明确报错");
    }

    [Fact]
    public async Task Execute_EmptyArgs_NoCurrentGoal_StartsGoalSpecCollection() {
        var engine = CreateEngine(currentState: null);
        var (cmd, context) = Arrange(arguments: "", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.RehydrateAsync(It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Once,
            "空参应先尝试从持久化恢复目标");
        engine.Verify(e => e.StartAsync(
            It.Is<string>(p => p.Contains("GoalSpec") && p.Contains("6 个字段")),
            It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once,
            "无当前目标应启动 GoalSpec 收集流程");
        output.Should().Contain("◎ /goal active — GoalSpec 收集模式");
        output.Should().Contain("  LLM 将逐个询问目标规格字段，收集完成后开始自主工作。");
        output.Should().Contain("  目标: 优化启动性能", "StartAsync 返回的状态应被渲染");
    }

    [Fact]
    public async Task Execute_EmptyArgs_WithCurrentGoal_ShowsStatusOnly() {
        var engine = CreateEngine(currentState: Goal());
        var (cmd, context) = Arrange(arguments: "", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));
        var clean = StripAnsi(output);

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never,
            "已有目标时不应重复启动收集");
        clean.Should().Contain("◎ /goal 运行中");
        output.Should().Contain("  目标: 优化启动性能");
        output.Should().Contain("  ID: goal-1");
        clean.Should().Contain("  状态: 运行中");
        output.Should().Contain("  轮数: 3");
        output.Should().Contain("  Token: 500 / 1000", "状态应渲染目标全貌");
        output.Should().Contain("  约束: 不许改公共 API");
    }

    [Fact]
    public async Task Execute_Pause_PausesGoal() {
        var engine = CreateEngine();
        var (cmd, context) = Arrange(arguments: "pause", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.PauseAsync(It.IsAny<CancellationToken>()), Times.Once, "pause 应暂停目标");
        output.Should().Contain("◎ /goal 已暂停");
    }

    [Fact]
    public async Task Execute_Resume_RehydratesThenResumes() {
        var engine = CreateEngine(currentState: null);
        var (cmd, context) = Arrange(arguments: "resume", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.RehydrateAsync(It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Once,
            "无当前目标时 resume 应先恢复持久化状态");
        engine.Verify(e => e.ResumeAsync(It.IsAny<CancellationToken>()), Times.Once, "resume 应恢复目标");
        output.Should().Contain("◎ /goal 已恢复");
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("stop")]
    [InlineData("off")]
    [InlineData("reset")]
    [InlineData("cancel")]
    public async Task Execute_ClearFamily_ClearsGoal(string arguments) {
        var engine = CreateEngine();
        var (cmd, context) = Arrange(arguments: arguments, engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.ClearAsync(It.IsAny<CancellationToken>()), Times.Once,
            $"{arguments} 应清除目标(语义等价别名族)");
        output.Should().Contain("目标已清除");
    }

    [Fact]
    public async Task Execute_WithObjective_ConstraintAndBudget_ParsedIntoStart() {
        var engine = CreateEngine();
        var (cmd, context) = Arrange(
            arguments: "--constraint '不许改配置' --budget 5000 优化性能",
            engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.StartAsync(
            It.Is<string>(p => p.Contains("优化性能")),
            It.IsAny<List<string>?>(),
            It.Is<int?>(b => b == 5000),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once, "--budget 应解析传入启动参数");
        output.Should().Contain("  初始提示: 优化性能");
        output.Should().Contain("  预填约束: 不许改配置", "引号约束应被剥离引号解析");
        output.Should().Contain("  预算: 5000 Token");
    }

    /// <summary>生产 bug 修复(TASK065 第五波): usage 文档形态 "/goal [初始提示] [--constraint ...] [--budget N]"
    /// 目标在前 — 选项解析应为位置无关剥离,objective 纯净不含选项原文,约束与预算正常提取传 StartAsync。</summary>
    [Fact]
    public async Task Execute_ObjectiveFirstDocForm_PassesCleanObjectiveToStart() {
        var engine = CreateEngine();
        var (cmd, context) = Arrange(
            arguments: "优化性能 --constraint '不许改配置' --budget 5000",
            engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.StartAsync(
            It.Is<string>(p => p.Contains("优化性能") && !p.Contains("--constraint") && !p.Contains("--budget")),
            It.IsAny<List<string>?>(),
            It.Is<int?>(b => b == 5000),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once,
            "目标在前的文档形态应剥离选项后传纯净 objective");
        output.Should().Contain("  初始提示: 优化性能", "初始提示不应混入选项原文");
        output.Should().Contain("  预填约束: 不许改配置");
        output.Should().Contain("  预算: 5000 Token");
    }

    [Fact]
    public async Task Execute_StartFailure_ConvergesToErrorHint() {
        var engine = CreateEngine();
        engine.Setup(e => e.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("goal already running"));
        var (cmd, context) = Arrange(arguments: "新目标", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("StartAsync 异常应收敛为错误提示而非向上抛出");
        output.Should().Contain("目标执行失败: goal already running");
    }

    [Fact]
    public async Task Execute_CronGoal_RegistersCronTask() {
        var engine = CreateEngine();
        var cronStore = new Mock<ICronTaskStore>();
        cronStore.Setup(s => s.AddTaskAsync(It.IsAny<CreateCronTaskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CronTask {
                Id = "cron-1",
                CronExpression = "*/5 * * * *",
                Prompt = "每五分钟检查构建",
                CreatedAt = 1000,
                IsRecurring = true,
                IsDurable = true,
            });
        var (cmd, context) = Arrange(
            arguments: "--cron */5 * * * * 每五分钟检查构建",
            engine: engine,
            cronStore: cronStore);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        cronStore.Verify(s => s.AddTaskAsync(
            It.Is<CreateCronTaskRequest>(r =>
                r.CronExpression == "*/5 * * * *"
                && r.Prompt == "每五分钟检查构建"
                && r.IsRecurring
                && r.IsDurable),
            It.IsAny<CancellationToken>()), Times.Once, "cron 模式应注册重复且持久的定时任务");
        output.Should().Contain("定时目标已注册");
        output.Should().Contain("  Cron: */5 * * * *");
        output.Should().Contain("  目标: 每五分钟检查构建");
        output.Should().Contain("  任务ID: cron-1");
    }

    [Fact]
    public async Task Execute_CronGoal_WithoutStore_ShowsError() {
        var engine = CreateEngine();
        var (cmd, context) = Arrange(
            arguments: "--cron */5 * * * * 描述",
            engine: engine,
            cronStore: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("错误: Cron 任务存储未注册", "存储缺失应明确报错");
    }

    [Fact]
    public async Task Execute_CronGoal_IncompleteExpression_ShowsError() {
        var engine = CreateEngine();
        var cronStore = new Mock<ICronTaskStore>();
        var (cmd, context) = Arrange(
            arguments: "--cron */5 * *",
            engine: engine,
            cronStore: cronStore);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("错误: 定时模式需要指定 Cron 表达式",
            "不足 5 字段的 cron 表达式应提示错误");
        cronStore.Verify(s => s.AddTaskAsync(It.IsAny<CreateCronTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never,
            "表达式不完整时不应注册任务");
    }

    [Fact]
    public async Task Execute_RegistryPreferred_EmptyGoals_StartsCollectionViaRegistry() {
        var registry = new Mock<IGoalRegistry>();
        registry.Setup(r => r.ListActiveGoalsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoalState>());
        registry.Setup(r => r.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Goal());
        var engine = CreateEngine();
        var (cmd, context) = Arrange(arguments: "", engine: engine, registry: registry);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        registry.Verify(r => r.RehydrateAllAsync(It.IsAny<CancellationToken>()), Times.Once,
            "registry 模式应先恢复全部持久化目标");
        registry.Verify(r => r.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once,
            "无活跃目标应经 registry 启动 GoalSpec 收集");
        engine.Verify(e => e.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never,
            "registry 存在时应优先走 registry 路径");
        output.Should().Contain("◎ /goal active — GoalSpec 收集模式");
    }

    [Fact]
    public async Task Execute_RegistryPreferred_ActiveGoals_ShowsGoalsList() {
        var registry = new Mock<IGoalRegistry>();
        registry.Setup(r => r.ListActiveGoalsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GoalState> {
                Goal(id: "goal-1", objective: "重构模块A"),
                Goal(id: "goal-2", objective: "修复内存泄漏", status: GoalStatus.Paused),
            });
        var (cmd, context) = Arrange(arguments: "", registry: registry);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));
        var clean = StripAnsi(output);

        result.ShouldContinue.Should().BeTrue();
        clean.Should().Contain("◎ 活跃目标列表 (2)");
        clean.Should().Contain("  [goal-1] 重构模块A — 运行中");
        clean.Should().Contain("  [goal-2] 修复内存泄漏 — 已暂停", "列表应渲染每个目标的运行状态");
    }

    [Fact]
    public async Task Execute_RegistryPause_PausesViaRegistry() {
        var registry = new Mock<IGoalRegistry>();
        registry.Setup(r => r.PauseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var (cmd, context) = Arrange(arguments: "pause", registry: registry);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        registry.Verify(r => r.PauseAsync(It.IsAny<CancellationToken>()), Times.Once,
            "registry 模式 pause 应委托注册表");
        output.Should().Contain("◎ /goal 已暂停");
    }

    /// <summary>生产 bug 登记(TASK065 brain 域,第五波已修复): usage 提示 "/goal [初始提示] [--constraint '约束'] [--budget N]"
    /// 的文档形态(目标在前、选项在后)曾不被 ParseGoalArgs 支持 — 选项剥离仅匹配 remaining 开头,
    /// 目标在前时整个字符串(含 --constraint/--budget 原文)作为 objective 传入 GoalSpecPromptBuilder。
    /// 修复后选项解析为位置无关全串扫描;"选项在前"既有形态与目标在前的文档形态均正确剥离。</summary>
    [Theory]
    [InlineData("--cron */5 * * * * 每五分钟检查", true, "*/5 * * * *", "每五分钟检查")]
    [InlineData("-c 0 9 * * 1 每周一早九点", true, "0 9 * * 1", "每周一早九点")]
    [InlineData("--cron */5 * *", true, null, "")]
    [InlineData("--constraint '不许改配置' --constraint 必须有测试 优化性能", false, null, "优化性能")]
    [InlineData("--budget 5000 优化性能", false, null, "优化性能")]
    [InlineData("--budget abc 优化性能", false, null, "优化性能")]
    [InlineData("纯目标文本", false, null, "纯目标文本")]
    public void ParseGoalArgs_ParsesCronConstraintsAndBudget(
        string args, bool expectCron, string? expectCronExpr, string expectObjective) {
        var parsed = GoalCommand.ParseGoalArgs(args);

        parsed.IsCron.Should().Be(expectCron, $"'{args}' 的 cron 判定应为 {expectCron}");
        if (expectCron) {
            parsed.CronExpression.Should().Be(expectCronExpr);
        } else {
            parsed.Objective.Should().Be(expectObjective);
        }
    }

    [Fact]
    public void ParseGoalArgs_ExtractsQuotedConstraintsAndBudget() {
        var parsed = GoalCommand.ParseGoalArgs("--constraint '引号约束' --constraint 单词约束 --budget 100 目标描述");

        parsed.Objective.Should().Be("目标描述");
        parsed.Constraints.Should().Equal("引号约束", "单词约束");
        parsed.TokenBudget.Should().Be(100);
        parsed.IsCron.Should().BeFalse();
    }

    [Fact]
    public void ParseGoalArgs_InvalidBudget_Ignored() {
        var parsed = GoalCommand.ParseGoalArgs("--budget abc 目标");

        parsed.TokenBudget.Should().BeNull("非数字预算应被忽略而非崩溃");
        parsed.Objective.Should().Be("目标");
    }

    /// <summary>生产 bug 修复(TASK065 第五波): 目标在前的文档形态 — 选项剥离应位置无关,
    /// 扫描全串提取 --constraint/--budget,objective 纯净不含选项原文。</summary>
    [Theory]
    [InlineData("优化性能 --constraint '不许改配置' --budget 5000", "优化性能", 5000, "不许改配置")]
    [InlineData("优化性能 --budget 5000", "优化性能", 5000, "")]
    [InlineData("优化性能 --constraint '不许改配置'", "优化性能", null, "不许改配置")]
    [InlineData("优化性能 --Constraint '大写选项' --Budget 99", "优化性能", 99, "大写选项")]
    [InlineData("优化性能 --constraint '步骤A' --constraint 步骤B --budget 100", "优化性能", 100, "步骤A|步骤B")]
    public void ParseGoalArgs_ObjectiveFirstDocForm_StripsOptionsAnywhere(
        string args, string expectObjective, int? expectBudget, string expectConstraints) {
        var parsed = GoalCommand.ParseGoalArgs(args);

        parsed.IsCron.Should().BeFalse($"'{args}' 不应误判为 cron 形态");
        parsed.Objective.Should().Be(expectObjective, "目标在前时 objective 应纯净不含选项原文");
        parsed.TokenBudget.Should().Be(expectBudget);
        List<string> expectList = expectConstraints.Length == 0 ? [] : [.. expectConstraints.Split('|')];
        parsed.Constraints.Should().Equal(expectList);
    }

    /// <summary>生产 bug 修复(TASK065 第五波): 选项夹在目标中间 — 剥离选项 token 后拼接剩余文本为 objective。</summary>
    [Fact]
    public void ParseGoalArgs_InlineOptions_JoinsRemainingObjectiveText() {
        var parsed = GoalCommand.ParseGoalArgs("重构模块A --constraint '不改API' 完成后通知 --budget 2000");

        parsed.Objective.Should().Be("重构模块A 完成后通知", "选项夹在目标中间应剥离并拼接剩余文本");
        parsed.Constraints.Should().Equal("不改API");
        parsed.TokenBudget.Should().Be(2000);
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new GoalCommand();
        cmd.Name.Should().Be("goal");
        cmd.Description.Should().Contain("目标自主循环");
        cmd.Usage.Should().StartWith("/goal [初始提示]");
        cmd.Usage.Should().Contain("pause").And.Contain("resume").And.Contain("clear").And.Contain("--cron");
    }
}
