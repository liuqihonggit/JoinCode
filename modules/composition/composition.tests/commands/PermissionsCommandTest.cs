namespace Composition.Tests.Commands;

/// <summary>
/// /permissions 命令单元测试 — 覆盖 list(规则按模式分组渲染)/add(allow 别名→Auto/ask→Ask/
/// 参数不足/无效模式)/remove(存在/不存在/参数不足)/clear(非交互确认拒绝降级)/
/// workspace(列表/添加含信任联动/目录缺失/参数不足/移除/清除)/未知操作
/// <para>断言以 mock 交互为主;L.T 本地化文本仅断言含输入参数的不变量部分</para>
/// <para>clear 确认经静态 Dialog.ConfirmAsync,测试环境非交互恒拒绝 — 确认后清除分支不可单测触达</para>
/// </summary>
[Collection("command-output")]
public sealed class PermissionsCommandTest {
    private static AgentPermissionRule CreateRule(string pattern, PermissionMode mode) =>
        new() { AgentPattern = pattern, Mode = mode, Level = PermissionLevel.Execute };

    private static (PermissionsCommand Command, ChatCommandContext Context, IFileSystem Fs) Arrange(
        string arguments,
        Mock<IAgentPermissionManager>? manager = null,
        Mock<IWorkspaceService>? workspace = null,
        Mock<ITrustFolderManager>? trustManager = null) {
        var fs = TestFileSystem.Current;
        var services = CommandTestHarness.CreateServices(
            fileSystem: fs,
            permissionManager: manager?.Object,
            workspaceService: workspace?.Object,
            configureDi: di => {
                if (trustManager is not null) di.AddSingleton(trustManager.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new PermissionsCommand(), context, fs);
    }

    [Fact]
    public async Task Execute_EmptyArgs_ListsRulesGroupedByMode() {
        var manager = new Mock<IAgentPermissionManager>();
        manager.Setup(m => m.ListRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentPermissionRule> {
                CreateRule("build-agent", PermissionMode.Auto),
                CreateRule("deploy-agent", PermissionMode.Ask),
            });
        var (cmd, context, _) = Arrange("", manager: manager);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.ListRulesAsync(It.IsAny<CancellationToken>()), Times.Once, "空参默认 list 应列出规则");
        output.Should().Contain("build-agent", "Auto 规则的 AgentPattern 应渲染");
        output.Should().Contain("deploy-agent", "Ask 规则的 AgentPattern 应渲染");
    }

    [Fact]
    public async Task Execute_List_WithoutManager_ShowsWarningWithoutThrow() {
        var (cmd, context, _) = Arrange("list", manager: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("管理器缺失应优雅降级而非抛异常");
        output.Should().NotBeEmpty("管理器缺失应有提示输出");
    }

    [Fact]
    public async Task Execute_Add_WithAllowAlias_CreatesAutoExecuteRule() {
        var manager = new Mock<IAgentPermissionManager>();
        AgentPermissionRule? captured = null;
        manager.Setup(m => m.AddRuleAsync(It.IsAny<AgentPermissionRule>(), It.IsAny<CancellationToken>()))
            .Callback<AgentPermissionRule, CancellationToken>((rule, _) => captured = rule)
            .Returns(Task.CompletedTask);
        var (cmd, context, _) = Arrange("add build-agent allow", manager: manager);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.AddRuleAsync(It.IsAny<AgentPermissionRule>(), It.IsAny<CancellationToken>()), Times.Once);
        captured.Should().NotBeNull();
        Assert.NotNull(captured);
        // "allow" 是 CLI 别名,语义等同 "auto" — PermissionBehavior.Allow → PermissionMode.Auto
        captured.Mode.Should().Be(PermissionMode.Auto);
        captured.Level.Should().Be(PermissionLevel.Execute, "Auto 模式默认 Level=Execute");
        captured.AgentPattern.Should().Be("build-agent");
        output.Should().Contain("build-agent", "成功提示应含规则模式");
    }

    [Fact]
    public async Task Execute_Add_WithAskModeAndDescription_CreatesAskReadRule() {
        var manager = new Mock<IAgentPermissionManager>();
        AgentPermissionRule? captured = null;
        manager.Setup(m => m.AddRuleAsync(It.IsAny<AgentPermissionRule>(), It.IsAny<CancellationToken>()))
            .Callback<AgentPermissionRule, CancellationToken>((rule, _) => captured = rule)
            .Returns(Task.CompletedTask);
        var (cmd, context, _) = Arrange("add deploy-agent ask \"部署前需确认\"", manager: manager);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        captured.Should().NotBeNull();
        Assert.NotNull(captured);
        captured.Mode.Should().Be(PermissionMode.Ask);
        captured.Level.Should().Be(PermissionLevel.Read, "非 Auto 模式默认 Level=Read");
        captured.AgentPattern.Should().Be("deploy-agent");
        captured.Description.Should().Be("部署前需确认", "add 第 4 参数起应合并为描述并去除引号");
    }

    [Fact]
    public async Task Execute_Add_TooFewArgs_ShowsUsageWithoutAdding() {
        var manager = new Mock<IAgentPermissionManager>();
        var (cmd, context, _) = Arrange("add only-pattern", manager: manager);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.AddRuleAsync(It.IsAny<AgentPermissionRule>(), It.IsAny<CancellationToken>()),
            Times.Never, "参数不足时不应添加规则");
    }

    [Fact]
    public async Task Execute_Add_InvalidMode_ShowsErrorWithoutAdding() {
        var manager = new Mock<IAgentPermissionManager>();
        var (cmd, context, _) = Arrange("add agent-x bogus", manager: manager);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.AddRuleAsync(It.IsAny<AgentPermissionRule>(), It.IsAny<CancellationToken>()),
            Times.Never, "无效模式不应添加规则");
        output.Should().Contain("bogus", "错误提示应包含无效的模式名");
    }

    [Fact]
    public async Task Execute_Remove_ExistingRule_RemovesIt() {
        var manager = new Mock<IAgentPermissionManager>();
        manager.Setup(m => m.RemoveRuleAsync("agent-x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var (cmd, context, _) = Arrange("remove agent-x", manager: manager);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.RemoveRuleAsync("agent-x", It.IsAny<CancellationToken>()), Times.Once);
        output.Should().Contain("agent-x", "移除成功提示应含模式名");
    }

    [Fact]
    public async Task Execute_Remove_MissingRule_ShowsNotFound() {
        var manager = new Mock<IAgentPermissionManager>();
        manager.Setup(m => m.RemoveRuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var (cmd, context, _) = Arrange("remove agent-x", manager: manager);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("agent-x", "未找到提示应含模式名");
    }

    [Fact]
    public async Task Execute_Remove_TooFewArgs_ShowsUsage() {
        var manager = new Mock<IAgentPermissionManager>();
        var (cmd, context, _) = Arrange("remove", manager: manager);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.RemoveRuleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "无模式名时不应发起移除");
    }

    [Fact]
    public async Task Execute_Clear_WithoutConfirmation_SkipsClearing() {
        var manager = new Mock<IAgentPermissionManager>();
        var (cmd, context, _) = Arrange("clear", manager: manager);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.ClearRulesAsync(It.IsAny<CancellationToken>()), Times.Never,
            "非交互环境确认拒绝,不应清空规则");
        output.Should().NotBeEmpty("取消应有提示输出");
    }

    [Fact]
    public async Task Execute_Workspace_List_ShowsCurrentDirectory() {
        var fs = TestFileSystem.Current;
        var (cmd, context, _) = Arrange("workspace list");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        var cwd = fs.GetCurrentDirectory();
        output.Should().Contain(cwd, "工作区列表应显示当前工作目录");
    }

    [Fact]
    public async Task Execute_Workspace_Add_ExistingDirectory_AddsAndTrusts() {
        var workspace = new Mock<IWorkspaceService>();
        workspace.Setup(w => w.AddDirectory(It.IsAny<string>())).Returns(true);
        var trustManager = new Mock<ITrustFolderManager>();
        var dir = Path.Combine(Path.GetTempPath(), "perm-ws-add");
        var (cmd, context, fs) = Arrange(
            $"workspace add {dir}",
            workspace: workspace,
            trustManager: trustManager);
        // 注意:TestFileSystem.Current 每次调用创建新实例,必须用 Arrange 返回的命令侧同一 fs 创建目录
        fs.CreateDirectory(dir);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        var fullPath = Path.GetFullPath(dir);
        workspace.Verify(w => w.AddDirectory(fullPath), Times.Once,
            "应添加 Path.GetFullPath 归一后的目录");
        trustManager.Verify(t => t.Trust(fullPath), Times.Once,
            "添加工作区目录成功时应联动信任该目录");
    }

    [Fact]
    public async Task Execute_Workspace_Add_MissingDirectory_ShowsError() {
        var workspace = new Mock<IWorkspaceService>();
        var (cmd, context, _) = Arrange(
            $"workspace add {Path.Combine(Path.GetTempPath(), "perm-ws-missing")}",
            workspace: workspace);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        workspace.Verify(w => w.AddDirectory(It.IsAny<string>()), Times.Never,
            "目录不存在时不应添加");
        output.Should().Contain("perm-ws-missing");
    }

    [Fact]
    public async Task Execute_Workspace_Add_TooFewArgs_ShowsUsage() {
        var workspace = new Mock<IWorkspaceService>();
        var (cmd, context, _) = Arrange("workspace add", workspace: workspace);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        workspace.Verify(w => w.AddDirectory(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Execute_Workspace_Remove_RemovesDirectory() {
        var workspace = new Mock<IWorkspaceService>();
        workspace.Setup(w => w.RemoveDirectory(It.IsAny<string>())).Returns(true);
        var dir = Path.Combine(Path.GetTempPath(), "perm-ws-rm");
        var (cmd, context, _) = Arrange($"workspace remove {dir}", workspace: workspace);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        workspace.Verify(w => w.RemoveDirectory(It.Is<string>(p => p == Path.GetFullPath(dir))), Times.Once,
            "移除应使用 Path.GetFullPath 归一后的目录");
    }

    [Fact]
    public async Task Execute_Workspace_Clear_ClearsDirectories() {
        var workspace = new Mock<IWorkspaceService>();
        var (cmd, context, _) = Arrange("workspace clear", workspace: workspace);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        workspace.Verify(w => w.Clear(), Times.Once, "clear 应清空额外工作区目录");
    }

    [Fact]
    public async Task Execute_UnknownAction_ShowsErrorWithActionName() {
        var (cmd, context, _) = Arrange("bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("bogus", "未知操作提示应包含操作名");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new PermissionsCommand();
        cmd.Name.Should().Be("permissions");
        cmd.Usage.Should().StartWith("/permissions");
        cmd.Aliases.Should().Contain("perm", "perm 为 /permissions 的别名");
    }
}
