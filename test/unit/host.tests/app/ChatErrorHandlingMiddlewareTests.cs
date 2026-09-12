namespace Host.Tests.App;

/// <summary>
/// ChatErrorHandlingMiddleware 错误分类测试 — 验证纵深防御的多级报错
/// </summary>
public sealed class ChatErrorHandlingMiddlewareTests
{
    [Fact]
    public void ClassifyException_ConfigurationException_ShouldPreserveType()
    {
        var original = JoinCode.Abstractions.Exceptions.ConfigurationException.Missing("OPENAI_API_KEY");

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        Assert.Same(original, result);
        Assert.IsType<JoinCode.Abstractions.Exceptions.ConfigurationException>(result);
    }

    [Fact]
    public void ClassifyException_HttpRequestException_429_ShouldReturnRateLimit()
    {
        var original = new System.Net.Http.HttpRequestException("Rate limited", null, System.Net.HttpStatusCode.TooManyRequests);

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Equal(429, apiEx.StatusCode);
        Assert.True(apiEx.IsRetryable);
    }

    [Fact]
    public void ClassifyException_HttpRequestException_401_ShouldReturnAuthentication()
    {
        var original = new System.Net.Http.HttpRequestException("Unauthorized", null, System.Net.HttpStatusCode.Unauthorized);

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Equal(401, apiEx.StatusCode);
        Assert.Equal("API005", apiEx.ErrorCode);
    }

    [Fact]
    public void ClassifyException_UnknownException_ShouldReturnWorkflowExecution()
    {
        var original = new InvalidOperationException("something broke");

        var result = JoinCode.Pipelines.Middlewares.ChatErrorHandlingMiddleware.ClassifyException(original);

        var apiEx = Assert.IsType<JoinCode.Abstractions.Exceptions.ApiException>(result);
        Assert.Equal("WF003", apiEx.ErrorCode);
    }
}
