// JCC11003 抑制: 边界可空抑制, 后续逐步修复
namespace Core.Tests.Web;

/// <summary>
/// SSRF 双闸中间件测试（B2, ADR 0140）— 验证:
/// ① WebSsrfGuardMiddleware DNS 失败/私网解析 fail-closed 拒绝
/// ② WebFetchMiddleware 每次请求(含重定向跟随)前复检解析结果
/// </summary>
public sealed class WebSsrfGuardMiddlewareTests {
    private static WebContext NewContext(string url) => new() {
        Url = url,
        UpgradedUrl = url,
    };

    private static Func<string, IPAddress[]> PublicDns(string ip) => _ => [IPAddress.Parse(ip)];

    // === WebSsrfGuardMiddleware ===

    [Fact]
    public async Task SsrfGuard_HostnameResolvesToPrivateIp_ShortsCircuit() {
        await using var middleware = new WebSsrfGuardMiddleware(dnsResolver: PublicDns("127.0.0.1"));
        var nextCalled = false;
        var context = NewContext("http://rebind.evil.example/");

        await middleware.InvokeAsync(context, (ctx, ct) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, default).ConfigureAwait(true);

        nextCalled.Should().BeFalse("私网 IP 必须在 SSRF 守卫层短路");
        context.Result.Should().NotBeNull();
        context.Result?.Success.Should().BeFalse();
        context.Result?.ErrorMessage.Should().Contain("SSRF");
        context.Result?.ErrorMessage.Should().Contain("rebind.evil.example", "必须说明触发参数");
    }

    [Fact]
    public async Task SsrfGuard_DnsFails_DeniesRequest() {
        // fail-closed: DNS 失败禁止放行(原缺陷 SocketException 被吞后放行)
        await using var middleware = new WebSsrfGuardMiddleware(dnsResolver: _ =>
            throw new SocketException((int)SocketError.HostNotFound));
        var nextCalled = false;
        var context = NewContext("http://unresolvable.invalid/");

        await middleware.InvokeAsync(context, (ctx, ct) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, default).ConfigureAwait(true);

        nextCalled.Should().BeFalse("DNS 解析失败必须拒绝");
        context.Result.Should().NotBeNull();
        context.Result?.Success.Should().BeFalse();
        context.Result?.ErrorMessage.Should().Contain("unresolvable.invalid");
    }

    [Fact]
    public async Task SsrfGuard_HostnameResolvesToPublicIp_PassesThrough() {
        await using var middleware = new WebSsrfGuardMiddleware(dnsResolver: PublicDns("203.0.113.7"));
        var nextCalled = false;
        var context = NewContext("http://public.example.com/");

        await middleware.InvokeAsync(context, (ctx, ct) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, default).ConfigureAwait(true);

        nextCalled.Should().BeTrue("公网主机必须放行到下一层");
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task SsrfGuard_DirectPrivateIpLiteral_ShortsCircuit() {
        await using var middleware = new WebSsrfGuardMiddleware();
        var context = NewContext("http://10.1.2.3/file");

        await middleware.InvokeAsync(context, (ctx, ct) => Task.CompletedTask, default).ConfigureAwait(true);

        context.Result.Should().NotBeNull();
        context.Result?.Success.Should().BeFalse();
    }

    // === WebFetchMiddleware: 请求前复检(TOCTOU 窗口收窄, 含重定向跟随) ===

    [Fact]
    public async Task Fetch_HostnameResolvesToPrivateIp_FailsBeforeRequest() {
        var apiClient = new Mock<IApiClient>();
        await using var middleware = new WebFetchMiddleware(
            apiClient.Object, dnsResolver: PublicDns("192.168.0.1"));
        var context = NewContext("http://late-rebind.evil.example/");

        await middleware.InvokeAsync(context, (ctx, ct) => Task.CompletedTask, default).ConfigureAwait(true);

        context.FetchResult.Should().NotBeNull();
        context.FetchResult?.Success.Should().BeFalse("请求发出前复检必须拦截私网解析");
        apiClient.Verify(a => a.SendAsync(It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()), Times.Never,
            "复检拒绝后不得发出任何请求");
        context.FetchResult?.ErrorMessage.Should().Contain("late-rebind.evil.example");
    }

    [Fact]
    public async Task Fetch_PublicIp_CallsApiClient() {
        var apiClient = new Mock<IApiClient>();
        apiClient.Setup(a => a.SendAsync(It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("network stub end"));
        await using var middleware = new WebFetchMiddleware(
            apiClient.Object, dnsResolver: PublicDns("8.8.4.4"));
        var context = NewContext("http://ok.example.com/page");

        await middleware.InvokeAsync(context, (ctx, ct) => Task.CompletedTask, default).ConfigureAwait(true);

        apiClient.Verify(a => a.SendAsync(It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fetch_RedirectFollowUp_RechecksResolution() {
        // 同主机 rebinding(TTL 过期): 首跳解析公网放行,重定向复检时同主机解析出私网,第二跳必须拦截
        var apiClient = new Mock<IApiClient>();
        var response301 = new HttpResponseMessage(HttpStatusCode.MovedPermanently) {
            Headers = { Location = new Uri("http://first.example.com/second") }
        };
        apiClient.Setup(a => a.SendAsync(It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response301);
        var resolveCount = 0;

        await using var middleware = new WebFetchMiddleware(apiClient.Object, dnsResolver: _ => {
            resolveCount++;
            return resolveCount == 1 ? [IPAddress.Parse("203.0.113.2")] : [IPAddress.Parse("10.2.3.4")];
        });
        var context = NewContext("http://first.example.com/start");

        await middleware.InvokeAsync(context, (ctx, ct) => Task.CompletedTask, default).ConfigureAwait(true);

        apiClient.Verify(a => a.SendAsync(It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()), Times.Once,
            "首跳放行,重定向复检解析为私网时必须拦截,不得发出第二个请求");
        context.FetchResult.Should().NotBeNull();
        context.FetchResult?.Success.Should().BeFalse(
            $"resolveCount={resolveCount} redirectUrl={context.FetchResult?.RedirectUrl} " +
            $"err={context.FetchResult?.ErrorMessage}");
        context.FetchResult?.ErrorMessage.Should().Contain("first.example.com");
    }
}
