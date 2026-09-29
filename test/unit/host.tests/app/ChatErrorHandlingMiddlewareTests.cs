namespace Host.Tests.App;

/// <summary>
/// ChatErrorHandlingMiddleware 错误分类测试 — 验证纵深防御的多级报错
/// </summary>
public sealed class ChatErrorHandlingMiddlewareTests {
    [Fact]
    public void ClassifyException_ConfigurationException_ShouldPreserveType() {
        var original = JoinCode.Abstractions.Exceptions.ConfigurationException.Missing("OPENAI_API_KEY");

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        Assert.Same(original, result);
        Assert.IsType<JoinCode.Abstractions.Exceptions.ConfigurationException>(result);
    }

    [Fact]
    public void ClassifyException_HttpRequestException_429_ShouldReturnRateLimit() {
        var original = new System.Net.Http.HttpRequestException("Rate limited", null, System.Net.HttpStatusCode.TooManyRequests);

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Equal(429, apiEx.StatusCode);
        Assert.True(apiEx.IsRetryable);
    }

    [Fact]
    public void ClassifyException_HttpRequestException_401_ShouldReturnAuthentication() {
        var original = new System.Net.Http.HttpRequestException("Unauthorized", null, System.Net.HttpStatusCode.Unauthorized);

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Equal(401, apiEx.StatusCode);
        Assert.Equal("API005", apiEx.ErrorCode);
    }

    [Fact]
    public void ClassifyException_UnknownException_ShouldReturnWorkflowExecution() {
        var original = new InvalidOperationException("something broke");

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Equal("WF003", apiEx.ErrorCode);
    }

    [Theory]
    [InlineData("连接 localhost:8080 失败", "本地服务")]
    [InlineData("连接 127.0.0.1:3000 失败", "本地服务")]
    [InlineData("api.openai.com 请求失败", "OpenAI")]
    [InlineData("api.anthropic.com 请求失败", "Anthropic")]
    [InlineData("未知端点请求失败", "API 服务")]
    [InlineData("", "API 服务")]
    public void GetEndpointHint_VariousMessages_ShouldReturnExpectedHint(string message, string expected) {
        var ex = new InvalidOperationException(message);

        var hint = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.GetEndpointHint(ex);

        Assert.Equal(expected, hint);
    }

    [Fact]
    public void ClassifyException_HttpRequestException_403_ShouldReturnAuthorization() {
        var original = new System.Net.Http.HttpRequestException("Forbidden", null, System.Net.HttpStatusCode.Forbidden);

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Equal(403, apiEx.StatusCode);
        Assert.Equal("API006", apiEx.ErrorCode);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.InternalServerError, 500)]
    [InlineData(System.Net.HttpStatusCode.BadGateway, 502)]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable, 503)]
    [InlineData(System.Net.HttpStatusCode.GatewayTimeout, 504)]
    public void ClassifyException_HttpRequestException_5xx_ShouldReturnResponseError(System.Net.HttpStatusCode status, int expectedCode) {
        var original = new System.Net.Http.HttpRequestException($"server error {expectedCode}", null, status);

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Equal(expectedCode, apiEx.StatusCode);
        Assert.Equal("API007", apiEx.ErrorCode);
    }

    [Fact]
    public void ClassifyException_HttpRequestException_NullStatusWithSocketException_ShouldReturnConnection() {
        var socketEx = new System.Net.Sockets.SocketException(10061);
        var original = new System.Net.Http.HttpRequestException("conn refused", socketEx, (System.Net.HttpStatusCode?)null);

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Null(apiEx.StatusCode);
        Assert.Equal("API002", apiEx.ErrorCode);
    }

    [Fact]
    public void ClassifyException_HttpRequestException_NullStatusWithTimeoutException_ShouldReturnTimeout() {
        var timeoutInner = new TimeoutException("timed out");
        var original = new System.Net.Http.HttpRequestException("request timed out", timeoutInner, (System.Net.HttpStatusCode?)null);

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Null(apiEx.StatusCode);
        Assert.Equal("API003", apiEx.ErrorCode);
    }

    [Fact]
    public void ClassifyException_HttpRequestException_NullStatusWithTaskCanceledException_ShouldReturnTimeout() {
        var cancelInner = new System.Threading.Tasks.TaskCanceledException();
        var original = new System.Net.Http.HttpRequestException("request cancelled", cancelInner, (System.Net.HttpStatusCode?)null);

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Null(apiEx.StatusCode);
        Assert.Equal("API003", apiEx.ErrorCode);
    }

    [Fact]
    public void ClassifyException_PureTimeoutException_ShouldReturnTimeout() {
        var original = new TimeoutException("api.openai.com timed out");

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Null(apiEx.StatusCode);
        Assert.Equal("API003", apiEx.ErrorCode);
        Assert.Equal("OpenAI", apiEx.Endpoint);
    }

    [Fact]
    public void ClassifyException_PureTaskCanceledException_ShouldReturnTimeout() {
        var original = new System.Threading.Tasks.TaskCanceledException();

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Null(apiEx.StatusCode);
        Assert.Equal("API003", apiEx.ErrorCode);
    }

    [Fact]
    public void ClassifyException_HttpRequestException_404_ShouldReturnConnectionDefaultBranch() {
        var original = new System.Net.Http.HttpRequestException("Not Found", null, System.Net.HttpStatusCode.NotFound);

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Null(apiEx.StatusCode);
        Assert.Equal("API002", apiEx.ErrorCode);
    }

    [Fact]
    public void ClassifyException_ApiException_ShouldPreserveType() {
        var original = JoinCode.Abstractions.Exceptions.ApiException.Connection("OpenAI");

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        Assert.Same(original, result);
        Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
    }

    [Fact]
    public void ClassifyException_WorkflowException_ShouldPreserveType() {
        var original = new JoinCode.Abstractions.Exceptions.WorkflowException("custom workflow error");

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        Assert.Same(original, result);
        Assert.IsType<JoinCode.Abstractions.Exceptions.WorkflowException>(result);
    }
}