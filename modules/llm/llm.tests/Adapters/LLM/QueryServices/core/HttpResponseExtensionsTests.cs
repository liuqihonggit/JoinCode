namespace JoinCode.Llm.Tests.Adapters.LLM.QueryServices.Core;

/// <summary>
/// HttpResponseExtensions.EnsureSuccessWithBodyAsync 单元测试
/// 验证：非 2xx 响应抛出的 HttpRequestException 保留 StatusCode（Bug 27/第27条修复）
/// </summary>
public sealed class HttpResponseExtensionsTests {
    /// <summary>
    /// 修复前：EnsureSuccessWithBodyAsync 抛的 HttpRequestException 不带 StatusCode，
    /// 导致 ShouldFallback 误判为连接级错误不 fallback。
    /// 修复后：应保留 StatusCode 供 ShouldFallback 按状态码匹配。
    /// </summary>
    [Fact]
    public async Task EnsureSuccessWithBodyAsync_NonSuccessResponse_PreservesStatusCode() {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) {
            Content = new StringContent("Service Unavailable")
        };

        var act = async () => await response.EnsureSuccessWithBodyAsync(CancellationToken.None).ConfigureAwait(true);

        var ex = await act.Should().ThrowAsync<HttpRequestException>().ConfigureAwait(true);
        ex.Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "修复后应保留 StatusCode 供 ShouldFallback 按状态码匹配决定是否 fallback");
    }

    [Fact]
    public async Task EnsureSuccessWithBodyAsync_SuccessResponse_DoesNotThrow() {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("{}")
        };

        await response.EnsureSuccessWithBodyAsync(CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task EnsureSuccessWithBodyAsync_ErrorResponse_MessageContainsStatusCodeAndBody() {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) {
            Content = new StringContent("rate limit exceeded")
        };

        var act = async () => await response.EnsureSuccessWithBodyAsync(CancellationToken.None).ConfigureAwait(true);

        var ex = await act.Should().ThrowAsync<HttpRequestException>().ConfigureAwait(true);
        ex.Which.Message.Should().Contain("429");
        ex.Which.Message.Should().Contain("rate limit exceeded");
    }
}
