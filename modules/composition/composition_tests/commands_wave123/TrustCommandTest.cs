namespace Composition.Tests.Commands;

/// <summary>
/// /trust 命令单元测试 — 覆盖 status(默认空参/已信任/未信任)/add/remove/list(空+存在性标记)/
/// clear(计数)/未知子命令用法提示/管理器缺失降级
/// <para>ITrustFolderManager 经 DI 容器解析,用 Moq 注入;目录存在性经 InMemoryFileSystem 验证</para>
/// </summary>
[Collection("command-output")]
public sealed class TrustCommandTest {
    private static (TrustCommand Command, Mock<ITrustFolderManager> Manager, ChatCommandContext Context) Arrange(
        string arguments = "",
        ITrustFolderManager? managerOverride = null,
        IReadOnlyList<string>? trustedFolders = null) {
        var manager = new Mock<ITrustFolderManager>();
        manager.Setup(m => m.GetAllTrustedFolders()).Returns(trustedFolders ?? []);
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(managerOverride ?? manager.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new TrustCommand(), manager, context);
    }

    [Fact]
    public async Task Execute_WithoutManager_ReturnsContinueSilently() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "add");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new TrustCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().NotContain("已信任", "管理器缺失时应静默降级,不执行任何信任操作");
    }

    [Fact]
    public async Task Execute_EmptyArgs_UntrustedWorkspace_ShowsStatusWithHint() {
        var (cmd, manager, context) = Arrange();
        manager.Setup(m => m.IsTrusted(It.IsAny<string>())).Returns(false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前工作区未信任");
        output.Should().Contain("使用 /trust add 添加信任", "未信任状态应引导用户添加信任");
        manager.Verify(m => m.IsTrusted(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Execute_EmptyArgs_TrustedWorkspace_ShowsTrustedStatus() {
        var (cmd, manager, context) = Arrange();
        manager.Setup(m => m.IsTrusted(It.IsAny<string>())).Returns(true);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前工作区已信任");
        output.Should().NotContain("使用 /trust add");
    }

    [Fact]
    public async Task Execute_Add_TrustsCurrentWorkspace() {
        var (cmd, manager, context) = Arrange(arguments: "add");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.Trust(It.IsAny<string>()), Times.Once, "add 应调用 Trust(当前工作区)");
        output.Should().Contain("已信任当前工作区: ");
    }

    [Fact]
    public async Task Execute_Remove_UntrustsCurrentWorkspace() {
        var (cmd, manager, context) = Arrange(arguments: "remove");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.Untrust(It.IsAny<string>()), Times.Once, "remove 应调用 Untrust(当前工作区)");
        output.Should().Contain("已移除工作区信任: ");
    }

    [Fact]
    public async Task Execute_List_NoFolders_ShowsEmptyMessage() {
        var (cmd, _, context) = Arrange(arguments: "list");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("暂无信任目录");
    }

    [Fact]
    public async Task Execute_List_MarksNonExistingDirectories() {
        var existingDir = Path.Combine(Path.GetTempPath(), "trust-exists-check");
        var missingDir = Path.Combine(Path.GetTempPath(), "trust-missing-check");
        var (cmd, _, context) = Arrange(
            arguments: "list",
            trustedFolders: [existingDir, missingDir]);
        // 注意:TestFileSystem.Current 每次调用创建新实例,直接操作命令侧 fs 使目录真实存在
        var services = context.GetCommandServices();
        services.FileSystem.CreateDirectory(existingDir);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("信任目录 (2 个)");
        output.Should().Contain(existingDir);
        output.Should().Contain($"{missingDir} [不存在]", "不存在的信任目录应带缺失标记");
    }

    [Fact]
    public async Task Execute_Clear_ClearsAllWithCount() {
        var (cmd, manager, context) = Arrange(
            arguments: "clear",
            trustedFolders: ["dir-a", "dir-b"]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.ClearAll(), Times.Once, "clear 应调用 ClearAll");
        output.Should().Contain("已清除所有信任目录 (2 个)", "清除提示应包含被清除的目录计数");
    }

    [Fact]
    public async Task Execute_UnknownAction_ShowsUsageGuide() {
        var (cmd, manager, context) = Arrange(arguments: "bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /trust [add|remove|list|clear]");
        output.Should().Contain("/trust add    信任当前工作区");
        output.Should().Contain("/trust clear  清除所有信任目录");
        manager.Verify(m => m.Trust(It.IsAny<string>()), Times.Never, "未知子命令不应触发任何信任操作");
        manager.Verify(m => m.ClearAll(), Times.Never);
    }

    [Fact]
    public async Task Execute_ActionIsCaseInsensitive() {
        var (cmd, manager, context) = Arrange(arguments: "ADD");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.Trust(It.IsAny<string>()), Times.Once, "子命令应 ToLowerInvariant 归一匹配");
        output.Should().Contain("已信任当前工作区: ");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new TrustCommand();
        cmd.Name.Should().Be("trust");
        cmd.Usage.Should().StartWith("/trust");
        cmd.Aliases.Should().BeEmpty();
    }
}
