namespace Composition.Tests.Commands;

/// <summary>
/// /logout 命令单元测试 — 覆盖 all 登出(Confirm 回调 true 执行/回调 false 拒绝/
/// 未注入回调时静态 Dialog 非交互确认拒绝降级)/OAuth Token 登出/
/// API Key 从 auth.json 移除(保留其余 provider)/未登录提示/provider 参数小写归一
/// <para>确认经 ChatCommandContext.Confirm 回调注入(对齐 CommitCommand 先例),
/// 未注入时降级静态 Dialog.ConfirmAsync — 测试环境(testhost)IsNonInteractive=true 恒拒绝</para>
/// </summary>
[Collection("command-output")]
public sealed class LogoutCommandTest {
    private static readonly string AuthPath = AppDataConstants.Paths.AuthFilePath;

    private static CommandServices CreateServices(
        IFileSystem fs,
        ITokenStorage? tokenStorage = null) {
        return CommandTestHarness.CreateServices(fileSystem: fs, tokenStorage: tokenStorage);
    }

    [Fact]
    public async Task Execute_AllWithoutConfirmation_ReturnsContinueAndKeepsAuthFile() {
        var fs = TestFileSystem.Current;
        await fs.WriteAllTextAsync(AuthPath, "{\"openai\":\"sk-key\"}");
        var services = CreateServices(fs);
        var context = CommandTestHarness.CreateContext(services, arguments: "all");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LogoutCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("非交互环境确认拒绝后应继续会话而非退出");
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("确定要登出所有服务吗？", "应弹出登出确认提示");
        fs.FileExists(AuthPath).Should().BeTrue("确认拒绝后不应删除 auth.json");
    }

    [Fact]
    public async Task Execute_AllWithConfirmCallbackTrue_PerformsLogoutAllAndExits() {
        var fs = TestFileSystem.Current;
        await fs.WriteAllTextAsync(AuthPath, "{\"openai\":\"sk-key\"}");
        var tokenStorage = new Mock<ITokenStorage>();
        tokenStorage.Setup(t => t.GetStoredProvidersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["openai"]);
        var services = CreateServices(fs, tokenStorage.Object);
        var confirmedMessages = new List<string>();
        var context = CommandTestHarness.CreateContext(
            services, arguments: "all", confirm: msg => { confirmedMessages.Add(msg); return true; });

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LogoutCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeFalse("确认通过后登出全部应退出应用");
        confirmedMessages.Should().ContainSingle("确认回调应被调用一次")
            .Which.Should().Contain("登出所有服务", "确认提示应说明登出范围");
        fs.FileExists(AuthPath).Should().BeFalse("确认通过后应删除 auth.json");
        tokenStorage.Verify(t => t.DeleteTokenAsync("openai", It.IsAny<CancellationToken>()), Times.Once,
            "确认通过后应删除全部已存储的 OAuth Token");
        output.Should().Contain("已登出所有服务", "确认通过后应输出登出成功提示");
    }

    [Fact]
    public async Task Execute_AllWithConfirmCallbackFalse_SkipsLogout() {
        var fs = TestFileSystem.Current;
        await fs.WriteAllTextAsync(AuthPath, "{\"openai\":\"sk-key\"}");
        var services = CreateServices(fs);
        var confirmedMessages = new List<string>();
        var context = CommandTestHarness.CreateContext(
            services, arguments: "all", confirm: msg => { confirmedMessages.Add(msg); return false; });

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(
            () => new LogoutCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("确认回调拒绝后应继续会话");
        confirmedMessages.Should().ContainSingle("确认回调应被调用一次");
        fs.FileExists(AuthPath).Should().BeTrue("确认回调拒绝后不应删除 auth.json");
    }

    [Fact]
    public async Task Execute_EmptyArgs_DefaultsToAllProvider() {
        var fs = TestFileSystem.Current;
        await fs.WriteAllTextAsync(AuthPath, "{\"openai\":\"sk-key\"}");
        var services = CreateServices(fs);
        var context = CommandTestHarness.CreateContext(services, arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LogoutCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("确定要登出所有服务吗？", "空参默认等价 all,应先确认");
        fs.FileExists(AuthPath).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_ProviderWithOAuthToken_DeletesTokenAndClearsCache() {
        var fs = TestFileSystem.Current;
        var tokenStorage = new Mock<ITokenStorage>();
        tokenStorage.Setup(t => t.HasTokenAsync("openai", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var services = CreateServices(fs, tokenStorage.Object);
        var context = CommandTestHarness.CreateContext(services, arguments: "OpenAI");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LogoutCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        tokenStorage.Verify(t => t.DeleteTokenAsync("openai", It.IsAny<CancellationToken>()), Times.Once,
            "指定 provider 登出应删除其 OAuth Token");
        output.Should().Contain("已登出 openai (OAuth)", "provider 参数应小写归一后输出");
        output.Should().Contain("已清除认证相关缓存", "登出后应刷新认证缓存");
    }

    [Fact]
    public async Task Execute_ProviderWithApiKey_RemovesFromAuthFileKeepingOthers() {
        var fs = TestFileSystem.Current;
        await fs.WriteAllTextAsync(AuthPath, "{\"openai\":\"sk-1\",\"anthropic\":\"sk-2\"}");
        var tokenStorage = new Mock<ITokenStorage>();
        tokenStorage.Setup(t => t.HasTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var services = CreateServices(fs, tokenStorage.Object);
        var context = CommandTestHarness.CreateContext(services, arguments: "openai");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LogoutCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已登出 openai");
        var authContent = await fs.ReadAllTextAsync(AuthPath);
        authContent.Should().NotContain("\"openai\"", "目标 provider 的 API Key 应从 auth.json 移除");
        authContent.Should().Contain("\"anthropic\"", "其余 provider 凭证应保留");
    }

    [Fact]
    public async Task Execute_ProviderNotLoggedIn_ShowsNotLoggedInMessage() {
        var fs = TestFileSystem.Current;
        var tokenStorage = new Mock<ITokenStorage>();
        tokenStorage.Setup(t => t.HasTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var services = CreateServices(fs, tokenStorage.Object);
        var context = CommandTestHarness.CreateContext(services, arguments: "deepseek");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LogoutCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未登录 deepseek", "无 Token 且无 API Key 时应提示未登录");
    }

    [Fact]
    public async Task Execute_ProviderMissingFromAuthFile_KeepsFileUnchanged() {
        var fs = TestFileSystem.Current;
        const string original = "{\"anthropic\":\"sk-2\"}";
        await fs.WriteAllTextAsync(AuthPath, original);
        var tokenStorage = new Mock<ITokenStorage>();
        tokenStorage.Setup(t => t.HasTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var services = CreateServices(fs, tokenStorage.Object);
        var context = CommandTestHarness.CreateContext(services, arguments: "openai");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new LogoutCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未登录 openai", "auth.json 中不含目标 provider 时应提示未登录");
        var authContent = await fs.ReadAllTextAsync(AuthPath);
        authContent.Should().Be(original, "移除失败(removed=false)时 auth.json 应保持原样");
    }

    [Fact]
    public async Task Execute_NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new LogoutCommand();
        cmd.Name.Should().Be("logout");
        cmd.Usage.Should().StartWith("/logout");
    }
}
