namespace Composition.Tests.Commands;

/// <summary>
/// /falv 命令单元测试 — 覆盖帮助(空参/-h)/引擎缺失降级/自由文本添加假定/--status 摘要与数据项渲染/
/// --budget 预算渲染(正常/耗尽)/--judge 裁决渲染/--evidence 证据链(空/非空)/--reset/
/// --continue 续费(未耗尽提示/耗尽续费/模式解析)/--cone 视锥/--conflict 冲突检测/元数据一致性
/// <para>ReasoningEngine 用 Moq 桩经 DI 注入,集合类方法全部 Setup 空返回避免 null 枚举崩溃</para>
/// </summary>
[Collection("command-output")]
public sealed class FalvCommandTest {
    /// <summary>剥离 ANSI 转义序列 — 预算/裁决/状态渲染带颜色码,断言基于纯文本</summary>
    private static string StripAnsi(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\x1B\[[0-9;]*m", string.Empty);

    /// <summary>构造全方法安全的引擎桩 — 集合方法返回空集合,摘要/预算返回默认实例</summary>
    private static Mock<IReasoningEngine> CreateEngine(
        ReasoningSummary? summary = null,
        BudgetStatus? budget = null,
        IReadOnlyList<DataItem>? items = null,
        IReadOnlyList<DataItem>? facts = null,
        IReadOnlyList<EvidenceRecord>? evidence = null) {
        var engine = new Mock<IReasoningEngine>();
        engine.Setup(e => e.GetSummary()).Returns(summary ?? new ReasoningSummary());
        engine.Setup(e => e.GetBudgetStatus()).Returns(budget ?? new BudgetStatus());
        engine.Setup(e => e.GetAllItems()).Returns((items ?? []).AsEnumerable());
        engine.Setup(e => e.GetFacts()).Returns((facts ?? []).AsEnumerable());
        engine.Setup(e => e.GetAllEvidence()).Returns((evidence ?? []).AsEnumerable());
        engine.Setup(e => e.AddAssumptionsAsync(It.IsAny<IReadOnlyList<DataItem>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        engine.Setup(e => e.RunAdversarialProcessAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        engine.Setup(e => e.ContinueAsync(It.IsAny<BudgetRefillMode?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        engine.Setup(e => e.ExpandFragment(It.IsAny<AgentRole>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns((ObservationFragment?)null);
        engine.Setup(e => e.DetectConeConflict(It.IsAny<AgentRole>(), It.IsAny<AgentRole>()))
            .Returns(new ConeConflictResult {
                RoleA = AgentRole.Prosecutor,
                RoleB = AgentRole.Defender,
                RoleAConclusions = [],
                RoleBConclusions = [],
                HasConflict = false,
            });
        return engine;
    }

    private static (FalvCommand Command, ChatCommandContext Context, Mock<IReasoningEngine> Engine) Arrange(
        string arguments = "",
        Mock<IReasoningEngine>? engine = null) {
        var reasoningEngine = engine ?? CreateEngine();
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(reasoningEngine.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new FalvCommand(), context, reasoningEngine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-h")]
    [InlineData("--help")]
    public async Task Execute_HelpAlias_ShowsUsageHelp(string arguments) {
        var (cmd, context, _) = Arrange(arguments: arguments);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /falv <假定内容> | /falv --status |", "空参与 -h/--help 应显示帮助");
        output.Should().Contain("结构化推理引擎 — 假定→验证→事实 三态跃迁");
        output.Should().Contain("--judge                         触发三权裁决（控方→辩方→法官）");
    }

    [Fact]
    public async Task Execute_WithoutEngine_ShowsError() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "--status");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new FalvCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("推理引擎未初始化", "引擎缺失应提示而非崩溃");
    }

    [Fact]
    public async Task Execute_FreeText_AddsAssumptionWithDefaults() {
        var engine = CreateEngine();
        var (cmd, context, _) = Arrange(arguments: "地球是圆的", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));
        var clean = StripAnsi(output);

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.AddAssumptionsAsync(
            It.Is<IReadOnlyList<DataItem>>(list =>
                list.Count == 1
                && list[0].Content == "地球是圆的"
                && list[0].State == DataState.Assumption
                && list[0].Source == "用户输入"),
            It.IsAny<CancellationToken>()), Times.Once, "自由文本应构造假定数据项入引擎");
        clean.Should().Contain("[假定] 地球是圆的");
    }

    [Fact]
    public async Task Execute_Status_RendersSummaryAndItems() {
        var engine = CreateEngine(
            summary: new ReasoningSummary {
                TotalAssumptions = 3,
                TotalVerified = 2,
                TotalFacts = 1,
                TotalRejected = 1,
                TotalPendingEvidence = 2,
                TotalEvidence = 4,
                LastRunAt = new DateTime(2026, 1, 15, 10, 30, 0),
            },
            items: new List<DataItem> {
                new() { Content = "已确认事实", State = DataState.Fact, Confidence = 95 },
                new() { Content = "待验证假定", State = DataState.Assumption, Confidence = 50 },
            });
        var (cmd, context, _) = Arrange(arguments: "--status", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));
        var clean = StripAnsi(output);

        result.ShouldContinue.Should().BeTrue();
        clean.Should().Contain("=== 推理引擎状态 ===");
        clean.Should().Contain("  假定: 3");
        clean.Should().Contain("  已验证: 2");
        clean.Should().Contain("  事实: 1");
        clean.Should().Contain("  被驳斥: 1");
        clean.Should().Contain("  待补充: 2");
        clean.Should().Contain("  证据总数: 4");
        clean.Should().Contain("  最近裁决: 10:30:00", "LastRunAt 有值时应渲染裁决时间");
        clean.Should().Contain("=== 预算状态 ===");
        clean.Should().Contain("=== 所有数据项 ===");
        clean.Should().Contain("[事实] 已确认事实 (置信度:95%)");
        clean.Should().Contain("[假定] 待验证假定 (置信度:50%)", "数据项应按状态标签渲染");
    }

    [Fact]
    public async Task Execute_Budget_RendersUsedAndRemaining() {
        var engine = CreateEngine(budget: new BudgetStatus {
            RoundsUsed = 2,
            RoundsBudget = 5,
            TokensUsed = 100,
            TokensBudget = 200,
        });
        var (cmd, context, _) = Arrange(arguments: "--budget", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));
        var clean = StripAnsi(output);

        result.ShouldContinue.Should().BeTrue();
        clean.Should().Contain("=== 预算状态 ===");
        clean.Should().Contain("轮次: 2/5 (剩余 3)");
        clean.Should().Contain("Token: 100/200 (剩余 100)", "预算应渲染已用/上限与剩余");
    }

    [Fact]
    public async Task Execute_BudgetExhausted_ShowsContinueHint() {
        var engine = CreateEngine(budget: new BudgetStatus {
            RoundsUsed = 5,
            RoundsBudget = 5,
            TokensUsed = 200,
            TokensBudget = 200,
        });
        var (cmd, context, _) = Arrange(arguments: "--budget", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("同时触底 — 使用 /falv --continue 续费", "双预算耗尽应提示续费方式");
    }

    [Fact]
    public async Task Execute_Judge_RunsAdversarialProcessAndRendersVerdicts() {
        var engine = CreateEngine(
            facts: new List<DataItem> { new() { Content = "裁决事实", State = DataState.Fact, Confidence = 90 } },
            items: new List<DataItem> {
                new() { Content = "被驳斥项", State = DataState.Rejected },
                new() { Content = "待补充项", State = DataState.PendingEvidence },
                new() { Content = "已验证项", State = DataState.Verified, Confidence = 80 },
            });
        var (cmd, context, _) = Arrange(arguments: "--judge", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));
        var clean = StripAnsi(output);

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.RunAdversarialProcessAsync(It.IsAny<CancellationToken>()), Times.Once,
            "--judge 应触发三权裁决流程");
        clean.Should().Contain("=== 裁决结果 ===");
        clean.Should().Contain("事实: 裁决事实 (置信度:90%)");
        clean.Should().Contain("被驳斥: 被驳斥项");
        clean.Should().Contain("待补充: 待补充项");
        clean.Should().Contain("已验证(部分接受): 已验证项 (置信度:80%)", "裁决应覆盖事实/驳斥/待补充/已验证四类");
    }

    [Fact]
    public async Task Execute_EvidenceEmpty_ShowsPlaceholder() {
        var (cmd, context, _) = Arrange(arguments: "--evidence");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 证据链 ===");
        output.Should().Contain("  暂无证据", "空证据链应显示占位提示");
    }

    [Fact]
    public async Task Execute_EvidenceWithRecords_RendersCategoryAndTrust() {
        var engine = CreateEngine(evidence: new List<EvidenceRecord> {
            new() {
                Content = "日志文件证明重启发生在 03:00",
                Category = EvidenceCategory.Digital,
                TrustLevel = TrustLevel.DirectEvidence,
                SubmittedBy = AgentRole.Prosecutor,
                IsUrlVerified = true,
            },
        });
        var (cmd, context, _) = Arrange(arguments: "--evidence", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 证据链 ===");
        output.Should().Contain("[Digital] 日志文件证明重启发生在 03:00 (信任度:DirectEvidence, 提交方:Prosecutor, URL验证:✓)",
            "证据应渲染分类/内容/信任度/提交方/URL验证状态");
    }

    [Fact]
    public async Task Execute_Reset_ResetsEngine() {
        var engine = CreateEngine();
        var (cmd, context, _) = Arrange(arguments: "--reset", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.Reset(), Times.Once, "--reset 应清空引擎状态");
        output.Should().Contain("推理引擎已重置 — DAG清空，预算恢复，视锥重置");
    }

    [Fact]
    public async Task Execute_Continue_BudgetNotExhausted_ShowsHintOnly() {
        var engine = CreateEngine(budget: new BudgetStatus {
            RoundsUsed = 1,
            RoundsBudget = 5,
            TokensUsed = 50,
            TokensBudget = 200,
        });
        var (cmd, context, _) = Arrange(arguments: "--continue", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("预算尚未耗尽 — 轮次:1/5 token:50/200");
        output.Should().Contain("仍可继续推理，输入 /falv --judge 即可", "未耗尽时应引导直接裁决而非续费");
        engine.Verify(e => e.ContinueAsync(It.IsAny<BudgetRefillMode?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never,
            "预算未耗尽时不应触发续费");
    }

    [Fact]
    public async Task Execute_Continue_Exhausted_RefillsAndContinues() {
        var engine = CreateEngine(budget: new BudgetStatus {
            RoundsUsed = 5,
            RoundsBudget = 5,
            TokensUsed = 200,
            TokensBudget = 200,
        });
        var (cmd, context, _) = Arrange(arguments: "--continue both", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));
        var clean = StripAnsi(output);

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.ContinueAsync(BudgetRefillMode.Both, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once,
            "--continue both 应以 Both 模式续费");
        clean.Should().Contain("[预算耗尽] 轮次和Token预算均耗尽");
        clean.Should().Contain("续费方式: Both", "续费方式回显解析后的枚举名");
        clean.Should().Contain("继续推理中...");
    }

    [Theory]
    [InlineData("--continue rounds", BudgetRefillMode.RoundsOnly)]
    [InlineData("--continue tokens", BudgetRefillMode.TokensOnly)]
    [InlineData("--continue default", BudgetRefillMode.Default)]
    [InlineData("--continue", BudgetRefillMode.Default)]
    public async Task Execute_ContinueModes_ParsedAndForwarded(string arguments, BudgetRefillMode expectedMode) {
        var engine = CreateEngine(budget: new BudgetStatus {
            RoundsUsed = 5,
            RoundsBudget = 5,
        });
        var (cmd, context, _) = Arrange(arguments: arguments, engine: engine);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.ContinueAsync(expectedMode, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once,
            $"'{arguments}' 应解析为 {expectedMode} 续费模式(缺省回退 default)");
    }

    [Fact]
    public async Task Execute_Cone_RendersRoleFragments() {
        var engine = CreateEngine();
        var (cmd, context, _) = Arrange(arguments: "--cone", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 有限视锥 ===");
        output.Should().Contain("[Prosecutor] 活跃片段:");
        output.Should().Contain("[Defender] 活跃片段:");
        output.Should().Contain("[Judge] 活跃片段:", "视锥应遍历全部三权角色");
        engine.Verify(e => e.ExpandFragment(It.IsAny<AgentRole>(), "", "*"), Times.AtLeast(3),
            "视锥渲染应对每个角色展开片段");
    }

    [Fact]
    public async Task Execute_Conflict_NoConflict_ShowsNone() {
        var engine = CreateEngine();
        var (cmd, context, _) = Arrange(arguments: "--conflict", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.DetectConeConflict(AgentRole.Prosecutor, AgentRole.Defender), Times.Once);
        engine.Verify(e => e.DetectConeConflict(AgentRole.Prosecutor, AgentRole.Judge), Times.Once);
        engine.Verify(e => e.DetectConeConflict(AgentRole.Defender, AgentRole.Judge), Times.Once,
            "冲突检测应覆盖控辩/控法官/辩法官三对组合");
        output.Should().Contain("=== 视锥冲突检测 ===");
        output.Should().Contain("冲突状态: 无冲突");
        output.Should().Contain("控方-法官冲突: 无");
        output.Should().Contain("辩方-法官冲突: 无");
    }

    [Fact]
    public async Task Execute_Conflict_WithConflict_ShowsWarning() {
        var engine = CreateEngine();
        engine.Setup(e => e.DetectConeConflict(AgentRole.Prosecutor, AgentRole.Defender))
            .Returns(new ConeConflictResult {
                RoleA = AgentRole.Prosecutor,
                RoleB = AgentRole.Defender,
                RoleAConclusions = ["控方结论A"],
                RoleBConclusions = ["辩方结论B"],
                HasConflict = true,
            });
        var (cmd, context, _) = Arrange(arguments: "--conflict", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));
        var clean = StripAnsi(output);

        result.ShouldContinue.Should().BeTrue();
        clean.Should().Contain("控方结论: 控方结论A");
        clean.Should().Contain("辩方结论: 辩方结论B");
        clean.Should().Contain("冲突状态: 存在冲突", "检测到冲突时应高亮警示");
    }

    [Fact]
    public async Task Execute_AddAssumption_BudgetExhausted_ShowsRefillHint() {
        var engine = CreateEngine(budget: new BudgetStatus {
            RoundsUsed = 5,
            RoundsBudget = 5,
            TokensUsed = 10,
            TokensBudget = 100,
        });
        var (cmd, context, _) = Arrange(arguments: "新假定", engine: engine);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));
        var clean = StripAnsi(output);

        result.ShouldContinue.Should().BeTrue();
        clean.Should().Contain("[假定] 新假定");
        clean.Should().Contain("⚠ 轮次预算耗尽", "添加假定后预算耗尽应附加续费提示");
        clean.Should().Contain("使用 /falv --continue [rounds|tokens|both|default] 续费并继续推理");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new FalvCommand();
        cmd.Name.Should().Be("falv");
        cmd.Description.Should().Contain("结构化推理");
        cmd.Usage.Should().StartWith("/falv <假定内容>");
        cmd.ArgumentHint.Should().Contain("--status").And.Contain("--judge");
    }
}
