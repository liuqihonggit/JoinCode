namespace Hands.Tests.Api;

public sealed class ApiExceptionTests
{
    [Theory]
    [InlineData(401, true)]
    [InlineData(403, false)]
    public void AuthException_ShouldSetProperties(int statusCode, bool isAuthentication)
    {
        var ex = new AuthException("/v1/chat", statusCode, "unauthorized", "response body");

        ex.StatusCode.Should().Be(statusCode);
        ex.Endpoint.Should().Be("/v1/chat");
        ex.ResponseContent.Should().Be("response body");
        ex.IsRetryable.Should().BeFalse();
        var expectedCode = isAuthentication
            ? global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiAuthentication.ToValue()
            : global::JoinCode.Abstractions.Exceptions.ErrorCode.ApiAuthorization.ToValue();
        ex.ErrorCode.Should().Be(expectedCode);
    }

    [Fact]
    public void RateLimitException_ShouldSetProperties()
    {
        var retryAfter = TimeSpan.FromSeconds(5);
        var ex = new RateLimitException("/v1/chat", retryAfter, "retry later");

        ex.StatusCode.Should().Be(429);
        ex.Endpoint.Should().Be("/v1/chat");
        ex.RetryAfter.Should().Be(retryAfter);
        ex.ResponseContent.Should().Be("retry later");
        ex.IsRetryable.Should().BeTrue();
        ex.SuggestedRetryCount.Should().Be(5);
    }

    [Fact]
    public void RateLimitException_WithoutRetryAfter_ShouldHaveNullRetryAfter()
    {
        var ex = new RateLimitException("/v1/chat");

        ex.RetryAfter.Should().BeNull();
        ex.Message.Should().Contain("请稍后重试");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void ServerErrorException_ShouldSetProperties(int statusCode)
    {
        var ex = new ServerErrorException("/v1/chat", statusCode, "server down");

        ex.StatusCode.Should().Be(statusCode);
        ex.Endpoint.Should().Be("/v1/chat");
        ex.ResponseContent.Should().Be("server down");
        ex.IsRetryable.Should().BeTrue();
        ex.SuggestedRetryCount.Should().Be(3);
    }

    [Fact]
    public void ValidationException_ShouldSetProperties()
    {
        var errors = new Dictionary<string, List<string>>
        {
            ["field1"] = ["error1", "error2"]
        };
        var ex = new global::Services.Api.ValidationException("/v1/chat", "bad request", errors, "response body");

        ex.StatusCode.Should().Be(400);
        ex.Endpoint.Should().Be("/v1/chat");
        ex.ResponseContent.Should().Be("response body");
        ex.IsRetryable.Should().BeFalse();
        ex.Errors.Should().ContainKey("field1");
        ex.Errors["field1"].Should().ContainInOrder("error1", "error2");
    }

    [Fact]
    public void ValidationException_WithoutErrors_ShouldHaveEmptyErrors()
    {
        var ex = new global::Services.Api.ValidationException("/v1/chat", "bad request");

        ex.Errors.Should().BeEmpty();
    }
}
