namespace Composition.Tests.Commands;

/// <summary>
/// /oauth-refresh 命令单元测试 — 覆盖三级前置降级(Token存储/客户端/配置缺失)/
/// 空参无存储 Provider 引导登录/多 Provider 默认刷新第一个/指定 Provider 无 Token/
/// 无 Refresh Token/刷新成功保存新 Token/刷新异常降级提示
/// <para>ITokenStorage 经 CommandServices 字段注入,IOAuthClient 与 IOptions&lt;OAuthOptions&gt; 经 DI 注入</para>
/// </summary>
[Collection("command-output")]
public sealed class OauthRefreshCommandTest {
    private static OAuthToken CreateToken(string? refreshToken = "refresh-secret") => new() {
        AccessToken = "access-old",
        RefreshToken = refreshToken,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
    };

    private static (OauthRefreshCommand Command, Mock<ITokenStorage> Storage, Mock<IOAuthClient> Client, ChatCommandContext Context) Arrange(
        string arguments = "",
        Mock<ITokenStorage>? storageOverride = null,
        bool withClient = true,
        bool withOptions = true,
        IReadOnlyList<string>? storedProviders = null,
        OAuthToken? storedToken = null,
        OAuthToken? refreshedToken = null,
        Exception? refreshError = null) {
        var storage = storageOverride ?? new Mock<ITokenStorage>();
        storage.Setup(s => s.GetStoredProvidersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedProviders ?? []);
        if (storedToken is not null) {
            storage.Setup(s => s.LoadTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(storedToken);
        }

        var client = new Mock<IOAuthClient>();
        if (refreshError is not null) {
            client.Setup(c => c.RefreshTokenAsync(It.IsAny<OAuthConfig>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(refreshError);
        } else {
            client.Setup(c => c.RefreshTokenAsync(It.IsAny<OAuthConfig>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(refreshedToken ?? new OAuthToken {
                    AccessToken = "access-new",
                    RefreshToken = "refresh-new",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
                });
        }

        var services = CommandTestHarness.CreateServices(
            tokenStorage: storage.Object,
            configureDi: di => {
                if (withClient) di.AddSingleton(client.Object);
                if (withOptions) di.AddSingleton(Microsoft.Extensions.Options.Options.Create(new OAuthOptions()));
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new OauthRefreshCommand(), storage, client, context);
    }

    [Fact]
    public async Task Execute_WithoutTokenStorage_ShowsStorageUnavailable() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "prov-x");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new OauthRefreshCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("OAuth Token 存储不可用");
    }

    [Fact]
    public async Task Execute_WithoutOAuthClient_ShowsClientUnavailable() {
        var (cmd, storage, _, context) = Arrange(arguments: "prov-x", withClient: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("OAuth 客户端不可用");
        storage.Verify(s => s.LoadTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "客户端缺失应在加载 Token 前终止");
    }

    [Fact]
    public async Task Execute_WithoutOptions_ShowsConfigUnavailable() {
        var (cmd, storage, _, context) = Arrange(arguments: "prov-x", withOptions: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("OAuth 配置不可用");
        storage.Verify(s => s.LoadTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_EmptyArgs_NoStoredProviders_ShowsLoginHint() {
        var (cmd, storage, client, context) = Arrange(storedProviders: []);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("无已存储的 OAuth Token");
        output.Should().Contain("/login --oauth");
        client.Verify(c => c.RefreshTokenAsync(It.IsAny<OAuthConfig>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "无 Provider 时不应发起刷新");
    }

    [Fact]
    public async Task Execute_EmptyArgs_MultipleProviders_PicksFirstAndRefreshes() {
        var (cmd, storage, client, context) = Arrange(
            storedProviders: ["prov-a", "prov-b"],
            storedToken: CreateToken());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("发现 2 个 Provider");
        output.Should().Contain("默认刷新: prov-a");
        storage.Verify(s => s.LoadTokenAsync("prov-a", It.IsAny<CancellationToken>()), Times.Once,
            "多 Provider 时应默认刷新第一个");
        client.Verify(c => c.RefreshTokenAsync(
            It.Is<OAuthConfig>(cfg => cfg.Provider == "prov-a"),
            "refresh-secret",
            It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(s => s.SaveTokenAsync("prov-a", It.IsAny<OAuthToken>(), It.IsAny<CancellationToken>()), Times.Once,
            "刷新成功后应保存新 Token");
        output.Should().Contain("Token 刷新成功");
    }

    [Fact]
    public async Task Execute_SingleProvider_LoadsItDirectly() {
        var (cmd, _, _, context) = Arrange(storedProviders: ["only-prov"], storedToken: CreateToken());

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().NotContain("发现", "单个 Provider 不应输出多 Provider 提示");
        output.Should().Contain("Token 刷新成功");
    }

    [Fact]
    public async Task Execute_ProviderWithoutStoredToken_ShowsHint() {
        var (cmd, _, client, context) = Arrange(arguments: "prov-x", storedToken: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("Provider 'prov-x' 无已存储的 Token");
        client.Verify(c => c.RefreshTokenAsync(It.IsAny<OAuthConfig>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_TokenWithoutRefreshToken_ShowsReloginHint() {
        var (cmd, _, client, context) = Arrange(arguments: "prov-x", storedToken: CreateToken(refreshToken: null));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("Provider 'prov-x' 无 Refresh Token");
        output.Should().Contain("/login --oauth");
        client.Verify(c => c.RefreshTokenAsync(It.IsAny<OAuthConfig>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "无 Refresh Token 不应发起刷新请求");
    }

    [Fact]
    public async Task Execute_RefreshSuccess_ShowsProviderAndExpiresAt() {
        var newToken = new OAuthToken {
            AccessToken = "access-new",
            RefreshToken = "refresh-new",
            ExpiresAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
        };
        var (cmd, storage, _, context) = Arrange(arguments: "prov-x", storedToken: CreateToken(), refreshedToken: newToken);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("Token 刷新成功");
        output.Should().Contain("Provider: prov-x");
        output.Should().Contain("2026-01-01 12:00:00 UTC", "过期时间应按 yyyy-MM-dd HH:mm:ss UTC 格式渲染");
        storage.Verify(s => s.SaveTokenAsync("prov-x", newToken, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_RefreshThrows_ShowsErrorAndReloginHint() {
        var (cmd, storage, _, context) = Arrange(
            arguments: "prov-x",
            storedToken: CreateToken(),
            refreshError: new InvalidOperationException("network down"));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("Token刷新失败");
        output.Should().Contain("network down");
        output.Should().Contain("/login --oauth 重新登录", "异常降级应引导用户重新登录");
        storage.Verify(s => s.SaveTokenAsync(It.IsAny<string>(), It.IsAny<OAuthToken>(), It.IsAny<CancellationToken>()),
            Times.Never, "刷新失败不应覆盖已有 Token");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new OauthRefreshCommand();
        cmd.Name.Should().Be("oauth-refresh");
        cmd.Usage.Should().Be("/oauth-refresh [provider]");
        cmd.IsHidden.Should().BeTrue();
    }
}
