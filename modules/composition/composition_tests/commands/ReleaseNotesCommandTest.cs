namespace Composition.Tests.Commands;

/// <summary>
/// /release-notes 命令单元测试 — 覆盖服务缺失降级(带版本/无版本)/空数据提示/
/// 多版本多行 Notes 渲染/服务异常收敛/元数据一致性
/// <para>ReleaseNotesService 用 Moq 桩经 DI 注入(GetService 解析)</para>
/// </summary>
[Collection("command-output")]
public sealed class ReleaseNotesCommandTest {
    private static (ReleaseNotesCommand Command, ChatCommandContext Context, Mock<IReleaseNotesService> Service) Arrange(
        string arguments = "",
        Mock<IReleaseNotesService>? service = null) {
        var releaseNotesService = service ?? new Mock<IReleaseNotesService>();
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(releaseNotesService.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new ReleaseNotesCommand(), context, releaseNotesService);
    }

    private static ReleaseInfo Release(string version, string notes, DateTime? publishedAt = null) => new() {
        Version = version,
        Notes = notes,
        PublishedAt = publishedAt ?? new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Execute_WithoutService_ShowsGitHubFallbackHint() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ReleaseNotesCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("服务缺失应降级提示而非崩溃");
        output.Should().Contain("发布说明 (当前版本:");
        output.Should().Contain("请访问 GitHub Releases 页面查看完整发布说明",
            "服务缺失应引导用户去 GitHub 查看而非空输出");
    }

    [Fact]
    public async Task Execute_WithoutService_WithVersion_ShowsVersionEcho() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "1.2.3");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ReleaseNotesCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("查看版本 1.2.3 的发布说明", "指定版本但服务缺失时应回显版本");
        output.Should().Contain("请访问 GitHub Releases 页面查看完整发布说明");
    }

    [Fact]
    public async Task Execute_EmptyReleases_ShowsNoDataHint() {
        var service = new Mock<IReleaseNotesService>();
        service.Setup(s => s.GetRecentReleasesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReleaseInfo>());
        var (cmd, context, _) = Arrange(service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("暂无发布说明数据");
        output.Should().Contain("请访问 GitHub Releases 页面查看完整发布说明");
        service.Verify(s => s.GetRecentReleasesAsync(5, It.IsAny<CancellationToken>()), Times.Once,
            "应请求最近 5 个版本");
    }

    [Fact]
    public async Task Execute_WithReleases_RendersVersionNotesAndDate() {
        var service = new Mock<IReleaseNotesService>();
        service.Setup(s => s.GetRecentReleasesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReleaseInfo> {
                Release("1.1.0", "新增 /falv 命令\n修复启动崩溃"),
                Release("1.0.0", "首个稳定版本"),
            });
        var (cmd, context, _) = Arrange(service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("Version 1.1.0:");
        output.Should().Contain("  · 新增 /falv 命令", "多行 Notes 应去空行逐条以 · 前缀渲染");
        output.Should().Contain("  · 修复启动崩溃");
        output.Should().Contain("  发布于: 2026-01-15", "发布时间应 yyyy-MM-dd 格式化");
        output.Should().Contain("Version 1.0.0:");
        output.Should().Contain("  · 首个稳定版本");
    }

    [Fact]
    public async Task Execute_ServiceFailure_ConvergesToErrorHint() {
        var service = new Mock<IReleaseNotesService>();
        service.Setup(s => s.GetRecentReleasesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("network down"));
        var (cmd, context, _) = Arrange(service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("服务异常应收敛为错误提示而非向上抛出");
        output.Should().Contain("获取发布说明失败: network down");
        output.Should().Contain("请访问 GitHub Releases 页面查看完整发布说明", "异常后仍应引导 GitHub 回退");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ReleaseNotesCommand();
        cmd.Name.Should().Be("release-notes");
        cmd.Usage.Should().Be("/release-notes [version]");
        cmd.Description.Should().Contain("发布说明");
        cmd.ArgumentHint.Should().Be("[version]");
        cmd.IsHidden.Should().BeTrue("release-notes 为隐藏命令");
    }
}
