namespace Pipelines.Tests;

/// <summary>
/// ChatErrorHandlingMiddleware 确定性单元测试 — mock next 委托驱动正常/异常分支，
/// 验证透传、异常分类转换、OperationCanceled 直接重抛。
/// 无 IO/时序依赖：next 桩用 await Task.CompletedTask 立即完成。
/// </summary>
public sealed class ChatErrorHandlingMiddlewareTest {
    private static ChatErrorHandlingMiddleware NewMiddleware(ILogger<ChatErrorHandlingMiddleware>? logger = null)
        => new(logger ?? NullLogger<ChatErrorHandlingMiddleware>.Instance);

    // === InvokeAsync: 正常流转 ===

    [Fact]
    public async Task InvokeAsync_NextReturnsEvents_PassesThroughAllInOrder() {
        await using var mw = NewMiddleware();
        var ctx = TestHelpers.NewContext();
        var events = new[] { ChatStreamEvent.Text("a"), ChatStreamEvent.Text("b"), ChatStreamEvent.Done() };

        var result = await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync(events, ct), default));

        result.Should().HaveCount(3);
        result[0].Content.Should().Be("a");
        result[1].Content.Should().Be("b");
        result[2].Type.Should().Be(ChatStreamEventType.Complete);
    }

    [Fact]
    public async Task InvokeAsync_EmptyNext_ReturnsEmpty() {
        await using var mw = NewMiddleware();
        var ctx = TestHelpers.NewContext();

        var result = await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync([], ct), default));

        result.Should().BeEmpty();
    }

    // === InvokeAsync: 异常分类 ===

    [Fact]
    public async Task InvokeAsync_NextThrowsOperationCanceled_RethrowsDirectlyWithoutClassification() {
        await using var mw = NewMiddleware();
        var ctx = TestHelpers.NewContext();

        var exception = await Record.ExceptionAsync(async () =>
            await TestHelpers.CollectAsync(
                mw.InvokeAsync(ctx, (_, ct) => TestHelpers.ThrowAsync(new OperationCanceledException(), ct), default)));

        exception.Should().BeOfType<OperationCanceledException>();
    }

    [Theory]
    [InlineData(401, "API005", "OpenAI")]
    [InlineData(403, "API006", "OpenAI")]
    [InlineData(429, "API004", "OpenAI")]
    [InlineData(500, "API007", "OpenAI")]
    [InlineData(503, "API007", "OpenAI")]
    public async Task InvokeAsync_NextThrowsHttpRequestStatusCode_ThrowsClassifiedApiException(
        int statusCode, string expectedErrorCode, string endpointHint) {
        await using var mw = NewMiddleware();
        var ctx = TestHelpers.NewContext();
        var httpEx = new System.Net.Http.HttpRequestException(
            "api.openai.com request failed", null, (System.Net.HttpStatusCode)statusCode);

        var exception = await Record.ExceptionAsync(async () =>
            await TestHelpers.CollectAsync(
                mw.InvokeAsync(ctx, (_, ct) => TestHelpers.ThrowAsync(httpEx, ct), default)));

        var apiEx = exception.Should().BeOfType<ApiException>().Which;
        apiEx.StatusCode.Should().Be(statusCode);
        apiEx.ErrorCode.Should().Be(expectedErrorCode);
        apiEx.Endpoint.Should().Be(endpointHint);
    }

    [Fact]
    public async Task InvokeAsync_NextThrowsTimeout_ThrowsApiTimeout() {
        await using var mw = NewMiddleware();
        var ctx = TestHelpers.NewContext();
        var timeoutEx = new TimeoutException("api.openai.com timed out");

        var exception = await Record.ExceptionAsync(async () =>
            await TestHelpers.CollectAsync(
                mw.InvokeAsync(ctx, (_, ct) => TestHelpers.ThrowAsync(timeoutEx, ct), default)));

        var apiEx = exception.Should().BeOfType<ApiException>().Which;
        apiEx.ErrorCode.Should().Be(ErrorCode.ApiTimeout.ToValue());
        apiEx.Endpoint.Should().Be("OpenAI");
    }

    [Fact]
    public async Task InvokeAsync_NextThrowsTaskCanceled_RethrowsDirectlyAsOperationCanceled() {
        await using var mw = NewMiddleware();
        var ctx = TestHelpers.NewContext();
        var canceledEx = new TaskCanceledException("api.anthropic.com canceled");

        var exception = await Record.ExceptionAsync(async () =>
            await TestHelpers.CollectAsync(
                mw.InvokeAsync(ctx, (_, ct) => TestHelpers.ThrowAsync(canceledEx, ct), default)));

        // TaskCanceledException 继承 OperationCanceledException，被 catch(OperationCanceledException) 直接重抛，不分类
        exception.Should().BeOfType<TaskCanceledException>();
    }

    [Fact]
    public async Task InvokeAsync_NextThrowsGenericException_ThrowsApiWorkflowExecution() {
        await using var mw = NewMiddleware();
        var ctx = TestHelpers.NewContext();
        var genericEx = new InvalidOperationException("boom");

        var exception = await Record.ExceptionAsync(async () =>
            await TestHelpers.CollectAsync(
                mw.InvokeAsync(ctx, (_, ct) => TestHelpers.ThrowAsync(genericEx, ct), default)));

        var apiEx = exception.Should().BeOfType<ApiException>().Which;
        apiEx.ErrorCode.Should().Be(ErrorCode.WorkflowExecution.ToValue());
    }

    [Fact]
    public async Task InvokeAsync_NextThrowsWorkflowException_PreservesOriginalInstance() {
        await using var mw = NewMiddleware();
        var ctx = TestHelpers.NewContext();
        var wfEx = new WorkflowException("custom workflow error", errorCode: "CUSTOM_CODE");

        var exception = await Record.ExceptionAsync(async () =>
            await TestHelpers.CollectAsync(
                mw.InvokeAsync(ctx, (_, ct) => TestHelpers.ThrowAsync(wfEx, ct), default)));

        exception.Should().BeSameAs(wfEx);
    }

    [Fact]
    public async Task InvokeAsync_EventsThenThrow_PassesEventsThenThrowsClassified() {
        await using var mw = NewMiddleware();
        var ctx = TestHelpers.NewContext();
        var events = new[] { ChatStreamEvent.Text("partial") };
        var httpEx = new System.Net.Http.HttpRequestException(
            "localhost failed", null, System.Net.HttpStatusCode.Unauthorized);

        var collected = new List<ChatStreamEvent>();
        var exception = await Record.ExceptionAsync(async () => {
            await foreach (var e in mw.InvokeAsync(
                ctx, (_, ct) => TestHelpers.EventsThenThrowAsync(events, httpEx, ct), default)) {
                collected.Add(e);
            }
        });

        collected.Should().HaveCount(1);
        collected[0].Content.Should().Be("partial");
        var apiEx = exception.Should().BeOfType<ApiException>().Which;
        apiEx.StatusCode.Should().Be(401);
        apiEx.Endpoint.Should().Be("本地服务");
    }

    // === ClassifyException: 纯函数确定性测试 ===

    [Fact]
    public void ClassifyException_WorkflowException_ReturnsSameInstance() {
        var wfEx = new WorkflowException("wf");
        ChatErrorHandlingMiddleware.ClassifyException(wfEx).Should().BeSameAs(wfEx);
    }

    [Fact]
    public void ClassifyException_TaskCanceled_ThrowsApiTimeout() {
        var canceledEx = new TaskCanceledException("api.anthropic.com canceled");
        var result = ChatErrorHandlingMiddleware.ClassifyException(canceledEx);
        var apiEx = result.Should().BeOfType<ApiException>().Which;
        apiEx.ErrorCode.Should().Be(ErrorCode.ApiTimeout.ToValue());
        apiEx.Endpoint.Should().Be("Anthropic");
    }

    [Fact]
    public void ClassifyException_TimeoutException_ThrowsApiTimeout() {
        var timeoutEx = new TimeoutException("api.openai.com timed out");
        var result = ChatErrorHandlingMiddleware.ClassifyException(timeoutEx);
        var apiEx = result.Should().BeOfType<ApiException>().Which;
        apiEx.ErrorCode.Should().Be(ErrorCode.ApiTimeout.ToValue());
        apiEx.Endpoint.Should().Be("OpenAI");
    }

    [Fact]
    public void ClassifyException_ApiException_ReturnsSameInstance() {
        var apiEx = ApiException.Timeout("endpoint");
        ChatErrorHandlingMiddleware.ClassifyException(apiEx).Should().BeSameAs(apiEx);
    }

    [Fact]
    public void ClassifyException_HttpRequestNullStatusWithSocketInner_ThrowsConnection() {
        var socketEx = new System.Net.Sockets.SocketException(10061);
        var httpEx = new System.Net.Http.HttpRequestException("localhost refused", socketEx, null);

        var result = ChatErrorHandlingMiddleware.ClassifyException(httpEx);

        var apiEx = result.Should().BeOfType<ApiException>().Which;
        apiEx.ErrorCode.Should().Be(ErrorCode.ApiConnection.ToValue());
    }

    [Fact]
    public void ClassifyException_HttpRequestNullStatusWithTimeoutInner_ThrowsTimeout() {
        var timeoutInner = new TimeoutException();
        var httpEx = new System.Net.Http.HttpRequestException("api.openai.com timeout", timeoutInner, null);

        var result = ChatErrorHandlingMiddleware.ClassifyException(httpEx);

        var apiEx = result.Should().BeOfType<ApiException>().Which;
        apiEx.ErrorCode.Should().Be(ErrorCode.ApiTimeout.ToValue());
    }

    [Fact]
    public void ClassifyException_HttpRequestUnknownStatus_ThrowsApiConnection() {
        var httpEx = new System.Net.Http.HttpRequestException(
            "network glitch", null, System.Net.HttpStatusCode.Conflict);

        var result = ChatErrorHandlingMiddleware.ClassifyException(httpEx);

        var apiEx = result.Should().BeOfType<ApiException>().Which;
        apiEx.ErrorCode.Should().Be(ErrorCode.ApiConnection.ToValue());
    }

    // === TASK069-E1: SSL/TLS 证书分类（对齐基准 formatAPIError isSSLError 分支）===

    [Fact]
    public void ClassifyException_HttpRequestWithAuthenticationInner_ThrowsSslCertificate() {
        var authInner = new System.Security.Authentication.AuthenticationException(
            "The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot");
        var httpEx = new System.Net.Http.HttpRequestException("api.anthropic.com handshake failed", authInner, null);

        var result = ChatErrorHandlingMiddleware.ClassifyException(httpEx);

        var apiEx = result.Should().BeOfType<ApiException>().Which;
        apiEx.ErrorCode.Should().Be(ErrorCode.ApiConnection.ToValue());
        apiEx.Endpoint.Should().Be("Anthropic");
        apiEx.Message.Should().Contain("SSL 证书校验失败");
        apiEx.Message.Should().Contain("系统证书存储"); // 可操作提示
    }

    [Fact]
    public void ClassifyException_SecureConnectionError_ThrowsSslCertificate() {
        var httpEx = new System.Net.Http.HttpRequestException(
            System.Net.Http.HttpRequestError.SecureConnectionError,
            "The SSL connection could not be established");

        var result = ChatErrorHandlingMiddleware.ClassifyException(httpEx);

        var apiEx = result.Should().BeOfType<ApiException>().Which;
        apiEx.Message.Should().Contain("SSL 证书校验失败");
    }

    [Theory]
    [InlineData("The remote certificate has expired", "SSL 证书已过期")]
    [InlineData("CERT_REVOKED: certificate revoked", "SSL 证书已被吊销")]
    [InlineData("RemoteCertificateNameMismatch api.example.com", "SSL 证书主机名不匹配")]
    [InlineData("DEPTH_ZERO_SELF_SIGNED_CERT", "检测到自签证书，证书链不受信任")]
    public void DescribeSslFailure_MapsKnownReasons(string innerMessage, string expected) {
        var authInner = new System.Security.Authentication.AuthenticationException(innerMessage);
        var httpEx = new System.Net.Http.HttpRequestException("handshake failed", authInner, null);

        ChatErrorHandlingMiddleware.DescribeSslFailure(httpEx).Should().Be(expected);
    }

    [Fact]
    public void IsSslFailure_NonSslSocketInner_ReturnsFalse() {
        var socketEx = new System.Net.Sockets.SocketException(10061);
        var httpEx = new System.Net.Http.HttpRequestException("localhost refused", socketEx, null);

        ChatErrorHandlingMiddleware.IsSslFailure(httpEx).Should().BeFalse();
    }

    // === GetEndpointHint: 纯函数确定性测试 ===

    [Theory]
    [InlineData("connection to localhost refused", "本地服务")]
    [InlineData("127.0.0.1 not responding", "本地服务")]
    [InlineData("api.openai.com 500", "OpenAI")]
    [InlineData("api.anthropic.com overload", "Anthropic")]
    [InlineData("unknown host", "API 服务")]
    public void GetEndpointHint_ReturnsHintByMessageContent(string message, string expected) {
        var ex = new Exception(message);
        ChatErrorHandlingMiddleware.GetEndpointHint(ex).Should().Be(expected);
    }
}
