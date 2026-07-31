namespace Infra.Tests.Utils.Resilience;

public sealed class ResilientHttpClientProviderTests
{
    [Fact]
    public void Implements_IResilientHttpClientProvider()
    {
        var inner = new Mock<IHttpClientProvider>().Object;
        var provider = new ResilientHttpClientProvider(inner);

        provider.Should().BeAssignableTo<IResilientHttpClientProvider>();
        provider.Should().BeAssignableTo<IHttpClientProvider>();
    }

    [Fact]
    public void GetClient_DelegatesToInner()
    {
        var expectedClient = new HttpClient();
        var mockInner = new Mock<IHttpClientProvider>();
        mockInner.Setup(x => x.GetClient()).Returns(expectedClient);

        var provider = new ResilientHttpClientProvider(mockInner.Object);
        var client = provider.GetClient();

        client.Should().BeSameAs(expectedClient);
    }

    [Fact]
    public void GetClient_WithName_DelegatesToInner()
    {
        var expectedClient = new HttpClient();
        var mockInner = new Mock<IHttpClientProvider>();
        mockInner.Setup(x => x.GetClient("test")).Returns(expectedClient);

        var provider = new ResilientHttpClientProvider(mockInner.Object);
        var client = provider.GetClient("test");

        client.Should().BeSameAs(expectedClient);
    }

    [Fact]
    public async Task SendResilientAsync_Success_ReturnsResponse()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "ok");
        var client = new HttpClient(handler);
        var mockInner = new Mock<IHttpClientProvider>();
        mockInner.Setup(x => x.GetClient()).Returns(client);

        var provider = new ResilientHttpClientProvider(mockInner.Object, policy: new ResiliencePolicy
        {
            Name = "test",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig { MaxRetries = 0 },
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        var response = await provider.SendResilientAsync(request, "test-op");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendResilientAsync_CircuitBreakerOpen_Throws()
    {
        var handler = new MockHttpMessageHandler(new HttpRequestException("connection refused"));
        var client = new HttpClient(handler);
        var mockInner = new Mock<IHttpClientProvider>();
        mockInner.Setup(x => x.GetClient()).Returns(client);

        var policy = new ResiliencePolicy
        {
            Name = "test-cb",
            OperationTimeout = TimeSpan.FromSeconds(1),
            CircuitBreaker = new CircuitBreakerConfig { FailureThreshold = 1, OpenDuration = TimeSpan.FromSeconds(10) },
        };
        var provider = new ResilientHttpClientProvider(mockInner.Object, policy: policy);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await provider.SendResilientAsync(request, "test-op"));

        var cb = provider.Executor.CircuitBreaker;
        cb.Should().NotBeNull();
        cb!.State.Should().Be(CircuitBreakerPhase.Open);
    }

    [Fact]
    public void Executor_ReturnsResilientHttpExecutor()
    {
        var inner = new Mock<IHttpClientProvider>().Object;
        var provider = new ResilientHttpClientProvider(inner);

        provider.Executor.Should().NotBeNull();
        provider.Executor.Should().BeOfType<ResilientHttpExecutor>();
    }

    [Fact]
    public async Task SendResilientAsync_WithDisabledRetry_NoRetry()
    {
        var handler = new MockHttpMessageHandler(new HttpRequestException("fail"));
        var client = new HttpClient(handler);
        var mockInner = new Mock<IHttpClientProvider>();
        mockInner.Setup(x => x.GetClient()).Returns(client);

        var policy = new ResiliencePolicy
        {
            Name = "test-no-retry",
            OperationTimeout = TimeSpan.FromSeconds(5),
            Retry = new RetryConfig { MaxRetries = 0 },
        };
        var provider = new ResilientHttpClientProvider(mockInner.Object, policy: policy);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await provider.SendResilientAsync(request, "test-op"));
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;

        public MockHttpMessageHandler(HttpStatusCode statusCode, string body = "")
        {
            _response = new HttpResponseMessage(statusCode) { Content = new StringContent(body) };
        }

        public MockHttpMessageHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_exception is not null) return Task.FromException<HttpResponseMessage>(_exception);
            return Task.FromResult(_response!);
        }
    }
}
