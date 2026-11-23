// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace Composition.Tests.Commands;

/// <summary>
/// /add-dir 命令单元测试 — 覆盖空参列目录/目录不存在/成功添加(信任联动+会话提示)/
/// 重复添加/--remember 持久化(含重复幂等)/工作区服务缺失降级/已信任不重复信任
/// <para>IWorkspaceService 经 CommandServices 字段注入;ITrustFolderManager 与 IConfigurationService 经 DI 注入;
/// 目录存在性经 InMemoryFileSystem 验证</para>
/// </summary>
[Collection("command-output")]
public sealed class AddDirCommandTest {
    private static (AddDirCommand Command, Mock<IWorkspaceService> Workspace, Mock<ITrustFolderManager> Trust, Mock<IConfigurationService> Config, ChatCommandContext Context, IFileSystem Fs) Arrange(
        string arguments = "",
        Mock<IWorkspaceService>? workspaceOverride = null,
        bool withWorkspace = true,
        Mock<ITrustFolderManager>? trustOverride = null,
        Mock<IConfigurationService>? configOverride = null,
        bool addReturns = true,
        IReadOnlyList<string>? additionalDirs = null) {
        var workspace = workspaceOverride ?? new Mock<IWorkspaceService>();
        workspace.Setup(w => w.AddDirectory(It.IsAny<string>())).Returns(addReturns);
        workspace.Setup(w => w.GetAdditionalDirectories()).Returns(additionalDirs ?? []);

        var trust = trustOverride ?? new Mock<ITrustFolderManager>();
        if (trustOverride is null) {
            trust.Setup(t => t.IsTrusted(It.IsAny<string>())).Returns(false);
        }

        var config = configOverride ?? new Mock<IConfigurationService>();
        if (configOverride is null) {
            config.Setup(c => c.GetAsync("permissions.additionalDirectories", It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
        }

        var fs = TestFileSystem.Current;
        var services = CommandTestHarness.CreateServices(
            fileSystem: fs,
            workspaceService: withWorkspace ? workspace.Object : null,
            configureDi: di => {
                di.AddSingleton(trust.Object);
                di.AddSingleton(config.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new AddDirCommand(), workspace, trust, config, context, fs);
    }

    [Fact]
    public async Task Execute_EmptyArgs_ListsCurrentDirectories() {
        var (cmd, _, _, _, context, _) = Arrange(arguments: "", additionalDirs: [@"C:\extra\a", @"C:\extra\b"]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("当前额外工作目录:");
        output.Should().Contain(@"C:\extra\a");
        output.Should().Contain(@"C:\extra\b");
    }

    [Fact]
    public async Task Execute_MissingDirectory_ShowsErrorWithoutAdding() {
        var (cmd, workspace, _, _, context, _) = Arrange(arguments: @"C:\no-such-dir");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("目录不存在: C:\\no-such-dir");
        workspace.Verify(w => w.AddDirectory(It.IsAny<string>()), Times.Never, "目录不存在时不应添加");
    }

    [Fact]
    public async Task Execute_ExistingDirectory_AddsAndTrustsDirectory() {
        var dir = Path.Combine(Path.GetTempPath(), "adddir-basic");
        var (cmd, workspace, trust, _, context, fs) = Arrange(arguments: dir);
        fs.CreateDirectory(dir);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        var fullPath = Path.GetFullPath(dir);
        workspace.Verify(w => w.AddDirectory(fullPath), Times.Once, "应添加 GetFullPath 归一后的目录");
        trust.Verify(t => t.Trust(fullPath), Times.Once, "添加成功时应联动信任该目录");
        output.Should().Contain($"已添加工作目录: {fullPath} (仅本次会话)");
        output.Should().Contain("/permissions workspace 管理工作区目录");
    }

    [Fact]
    public async Task Execute_AlreadyAddedDirectory_ShowsExistsWithoutRetrust() {
        var dir = Path.Combine(Path.GetTempPath(), "adddir-dup");
        var (cmd, workspace, trust, _, context, fs) = Arrange(arguments: dir, addReturns: false);
        fs.CreateDirectory(dir);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain($"目录已存在: {Path.GetFullPath(dir)}");
        trust.Verify(t => t.Trust(It.IsAny<string>()), Times.Never, "重复添加不应再次触发信任");
    }

    [Fact]
    public async Task Execute_WithRemember_PersistsToLocalSettings() {
        var dir = Path.Combine(Path.GetTempPath(), "adddir-remember");
        var (cmd, _, _, config, context, fs) = Arrange(arguments: $"{dir} --remember");
        fs.CreateDirectory(dir);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        var fullPath = Path.GetFullPath(dir);
        config.Verify(c => c.SetAsync("permissions.additionalDirectories", fullPath, It.IsAny<CancellationToken>()),
            Times.Once, "--remember 应把目录追加持久化到本地设置");
        output.Should().Contain($"已添加工作目录: {fullPath} (已保存到本地设置)");
    }

    [Fact]
    public async Task Execute_RememberAlreadyPersisted_DoesNotDuplicate() {
        var dir = Path.Combine(Path.GetTempPath(), "adddir-remember-dup");
        var fullPath = Path.GetFullPath(dir);
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("permissions.additionalDirectories", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fullPath);
        var (cmd, _, _, _, context, fs) = Arrange(arguments: $"{dir} --remember", configOverride: config);
        fs.CreateDirectory(dir);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "目录已在持久化列表时不应重复写入相同目录");
    }

    [Fact]
    public async Task Execute_WithoutWorkspaceService_DoesNotThrow() {
        var dir = Path.Combine(Path.GetTempPath(), "adddir-nows");
        var (cmd, workspace, _, _, context, fs) = Arrange(arguments: dir, withWorkspace: false);
        fs.CreateDirectory(dir);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("工作区服务缺失应优雅降级而非抛异常");
        workspace.Verify(w => w.AddDirectory(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Execute_TrustedDirectory_DoesNotRetrust() {
        var dir = Path.Combine(Path.GetTempPath(), "adddir-trusted");
        var trust = new Mock<ITrustFolderManager>();
        trust.Setup(t => t.IsTrusted(It.IsAny<string>())).Returns(true);
        var (cmd, _, trustMock, _, context, fs) = Arrange(arguments: dir, trustOverride: trust);
        fs.CreateDirectory(dir);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        trustMock.Verify(t => t.Trust(It.IsAny<string>()), Times.Never, "目录已信任时不应重复调用 Trust");
        output.Should().Contain("已添加工作目录:", "已信任不影响目录添加主路径");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new AddDirCommand();
        cmd.Name.Should().Be("add-dir");
        cmd.Usage.Should().Be("/add-dir <path> [--remember]");
    }
}
